using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskLit;

internal static class NativeScanner
{
    const int KeepLargest = 10_000;
    const int ProgressIntervalMilliseconds = 650;
    const int PartialResultsIntervalMilliseconds = 2_000;
    const int FolderDepthForFilter = 4;
    const int FolderLimitForFilter = 2_000;
    const long MaxFileTime = 2_650_467_743_999_999_999;

    public static ScanResult Scan(string root, CancellationToken token, IProgress<ScanProgress> progress)
    {
        var state = new ScanState(progress);
        var solidState = DriveMediaDetector.IsSolidState(root);
        var workers = solidState ? Math.Clamp(Environment.ProcessorCount / 2, 2, 6) : 1;

        Traverse(root, state, workers, token);

        var result = state.ToResult(root, solidState);
        state.Report(root, result.Largest);
        return result;
    }

    static void Traverse(string root, ScanState state, int workers, CancellationToken token)
    {
        var queue = new ConcurrentStack<(string Path, int Folder)>();
        using var available = new SemaphoreSlim(0);
        var pending = 1;
        var completed = 0;
        ExceptionDispatchInfo? failure = null;
        queue.Push((root, state.Folders.Register(-1, "")));
        available.Release();

        void Work()
        {
            try
            {
                var children = new List<(string Path, int Folder)>(64);
                var categories = new long[FileTypes.All.Length];
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    available.Wait(token);
                    if (Volatile.Read(ref completed) != 0) return;
                    if (!queue.TryPop(out var item)) continue;

                    children.Clear();
                    Array.Clear(categories);
                    EnumerateDirectory(item.Path, item.Folder, state, children.Add, categories, token);
                    state.AddCategories(categories);
                    if (children.Count > 0)
                    {
                        Interlocked.Add(ref pending, children.Count);
                        foreach (var child in children)
                        {
                            queue.Push(child);
                            available.Release();
                        }
                    }

                    state.ReportIfDue(item.Path);
                    if (Interlocked.Decrement(ref pending) != 0) continue;
                    Volatile.Write(ref completed, 1);
                    available.Release(workers);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                if (Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(error), null) is not null) return;
                Volatile.Write(ref completed, 1);
                available.Release(workers);
            }
        }

        if (workers == 1)
        {
            Work();
            return;
        }

        var tasks = new Task[workers];
        for (var index = 0; index < workers; index++)
            tasks[index] = Task.Factory.StartNew(Work, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        try
        {
            Task.WaitAll(tasks, CancellationToken.None);
        }
        catch (AggregateException error) when (error.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
            throw new OperationCanceledException(token);
        }

        failure?.Throw();
    }

    static void EnumerateDirectory(string directory, int folder, ScanState state,
        Action<(string Path, int Folder)> addDirectory, long[] categories, CancellationToken token)
    {
        using var handle = NativeMethods.FindFirstFileEx(ToExtendedPath(Path.Join(directory, "*")),
            NativeMethods.FindInfoLevels.Basic, out var data, NativeMethods.FindSearchOps.NameMatch,
            IntPtr.Zero, NativeMethods.FindFirstExLargeFetch);
        if (handle.IsInvalid)
        {
            state.AddError();
            return;
        }

        long ownBytes = 0;
        long ownFiles = 0;

        do
        {
            token.ThrowIfCancellationRequested();
            var name = data.FileName;
            if (name is "." or "..") continue;
            if (IsTraversalLoop(data)) continue;

            var fullPath = Path.Join(directory, name);
            if ((data.Attributes & FileAttributes.Directory) != 0)
            {
                addDirectory((fullPath, state.Folders.Register(folder, name)));
                continue;
            }

            var size = ((long)data.FileSizeHigh << 32) | data.FileSizeLow;
            ownBytes += size;
            ownFiles++;
            categories[(int)FileTypes.Classify(name)] += size;
            state.AddFile(new FileEntry(name, fullPath, size, ToLocalTime(data.LastWriteTime)));
        }
        while (NativeMethods.FindNextFile(handle, out data));

        if (Marshal.GetLastWin32Error() != NativeMethods.ErrorNoMoreFiles) state.AddError();
        state.Folders.SetOwnTotals(folder, ownBytes, ownFiles);
    }

    static bool IsTraversalLoop(in NativeMethods.FindData data) =>
        (data.Attributes & FileAttributes.ReparsePoint) != 0
        && data.Reserved0 is NativeMethods.ReparseTagMountPoint or NativeMethods.ReparseTagSymlink;

    static DateTime ToLocalTime(System.Runtime.InteropServices.ComTypes.FILETIME time)
    {
        var raw = ((long)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;
        return raw > 0 && raw <= MaxFileTime ? DateTime.FromFileTimeUtc(raw).ToLocalTime() : DateTime.MinValue;
    }

    static string ToExtendedPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + path[2..];
        return @"\\?\" + path;
    }

    sealed class ScanState(IProgress<ScanProgress> progress)
    {
        readonly PriorityQueue<FileEntry, long> largest = new();
        readonly object heapLock = new();
        readonly Stopwatch elapsed = Stopwatch.StartNew();
        long files;
        long bytes;
        long errors;
        long nextReportAt = ProgressIntervalMilliseconds;
        long nextPartialResultsAt = PartialResultsIntervalMilliseconds;

        readonly long[] categoryBytes = new long[FileTypes.All.Length];

        public FolderIndex Folders { get; } = new();

        public void AddCategories(long[] local)
        {
            for (var index = 0; index < local.Length; index++)
                if (local[index] > 0)
                    Interlocked.Add(ref categoryBytes[index], local[index]);
        }

        long[] CategorySnapshot()
        {
            var copy = new long[categoryBytes.Length];
            for (var index = 0; index < copy.Length; index++) copy[index] = Interlocked.Read(ref categoryBytes[index]);
            return copy;
        }

        public void AddFile(FileEntry entry)
        {
            Interlocked.Increment(ref files);
            Interlocked.Add(ref bytes, entry.Size);
            lock (heapLock)
            {
                if (largest.Count < KeepLargest) largest.Enqueue(entry, entry.Size);
                else if (largest.TryPeek(out _, out var smallest) && entry.Size > smallest)
                {
                    largest.Dequeue();
                    largest.Enqueue(entry, entry.Size);
                }
            }
        }

        public void AddError() => Interlocked.Increment(ref errors);

        public void ReportIfDue(string current)
        {
            var now = elapsed.ElapsedMilliseconds;
            var due = Volatile.Read(ref nextReportAt);
            if (now < due || Interlocked.CompareExchange(ref nextReportAt, now + ProgressIntervalMilliseconds, due) != due) return;
            var snapshotDue = Volatile.Read(ref nextPartialResultsAt);
            var includeLargest = now >= snapshotDue
                && Interlocked.CompareExchange(ref nextPartialResultsAt, now + PartialResultsIntervalMilliseconds, snapshotDue) == snapshotDue;
            Report(current, includeLargest ? Snapshot() : null);
        }

        public void Report(string current, List<FileEntry>? snapshot) => progress.Report(new ScanProgress
        {
            Files = Interlocked.Read(ref files),
            Bytes = Interlocked.Read(ref bytes),
            Current = current,
            Inaccessible = Interlocked.Read(ref errors),
            Largest = snapshot
        });

        public ScanResult ToResult(string root, bool solidState) => new(
            Snapshot(),
            Interlocked.Read(ref files),
            Interlocked.Read(ref bytes),
            Interlocked.Read(ref errors),
            elapsed.Elapsed,
            Folders.Rollup(root, FolderDepthForFilter, FolderLimitForFilter),
            CategorySnapshot(),
            Folders.Count,
            root,
            solidState);

        List<FileEntry> Snapshot()
        {
            lock (heapLock)
                return largest.UnorderedItems.Select(item => item.Element).OrderByDescending(file => file.Size).ToList();
        }
    }

    static class DriveMediaDetector
    {
        const uint NoAccess = 0;
        const uint FileShareRead = 1;
        const uint FileShareWrite = 2;
        const uint OpenExisting = 3;
        const uint IoctlStorageQueryProperty = 0x002D1400;
        const int StorageDeviceSeekPenaltyProperty = 7;
        const int PropertyStandardQuery = 0;

        public static bool IsSolidState(string root)
        {
            try
            {
                var drive = Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar);
                if (string.IsNullOrEmpty(drive)) return false;
                using var handle = CreateFile($@"\\.\{drive}", NoAccess, FileShareRead | FileShareWrite,
                    IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (handle.IsInvalid) return false;
                var query = new StoragePropertyQuery
                {
                    PropertyId = StorageDeviceSeekPenaltyProperty,
                    QueryType = PropertyStandardQuery
                };
                return DeviceIoControl(handle, IoctlStorageQueryProperty, ref query, Marshal.SizeOf<StoragePropertyQuery>(),
                    out DeviceSeekPenaltyDescriptor descriptor, Marshal.SizeOf<DeviceSeekPenaltyDescriptor>(), out _, IntPtr.Zero)
                    && !descriptor.IncursSeekPenalty;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct StoragePropertyQuery
        {
            public int PropertyId;
            public int QueryType;
            public byte AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DeviceSeekPenaltyDescriptor
        {
            public uint Version;
            public uint Size;
            [MarshalAs(UnmanagedType.U1)] public bool IncursSeekPenalty;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DeviceIoControl(SafeFileHandle device, uint code, ref StoragePropertyQuery input, int inputSize,
            out DeviceSeekPenaltyDescriptor output, int outputSize, out uint returned, IntPtr overlapped);
    }

    static class NativeMethods
    {
        public const int ErrorNoMoreFiles = 18;
        public const int FindFirstExLargeFetch = 2;
        public const uint ReparseTagMountPoint = 0xA000_0003;
        public const uint ReparseTagSymlink = 0xA000_000C;

        public enum FindInfoLevels { Standard, Basic }

        public enum FindSearchOps { NameMatch, LimitToDirectories, LimitToDevices }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct FindData
        {
            public FileAttributes Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint Reserved0;
            public uint Reserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string FileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string AlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern SafeFindHandle FindFirstFileEx(string fileName, FindInfoLevels infoLevel,
            out FindData findData, FindSearchOps searchOp, IntPtr searchFilter, int additionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FindNextFile(SafeFindHandle handle, out FindData findData);
    }

    sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        SafeFindHandle() : base(true) { }

        protected override bool ReleaseHandle() => FindClose(handle);

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool FindClose(IntPtr handle);
    }
}
