using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskLit;

internal sealed record ProcessEntry(int Id, string Name, double CpuPercent, long WorkingSet, string ExecutablePath);

internal sealed class ProcessMonitor : IDisposable
{
    const int SystemProcessInformation = 5;
    const uint StatusInfoLengthMismatch = 0xC0000004;
    const uint QueryLimitedInformation = 0x1000;

    readonly Dictionary<int, (long Cpu, long Created)> previous = [];
    readonly Dictionary<int, (long Created, string Path)> pathCache = [];
    IntPtr buffer = Marshal.AllocHGlobal(512 * 1024);
    int capacity = 512 * 1024;
    long previousTimestamp;

    public bool Available { get; private set; } = true;

    public void Release()
    {
        previous.Clear();
        previous.TrimExcess();
        pathCache.Clear();
        pathCache.TrimExcess();
        previousTimestamp = 0;

        if (buffer == IntPtr.Zero) return;
        Marshal.FreeHGlobal(buffer);
        buffer = IntPtr.Zero;
        capacity = 0;
    }

    public IReadOnlyList<ProcessEntry> Sample()
    {
        if (buffer == IntPtr.Zero)
        {
            capacity = 512 * 1024;
            buffer = Marshal.AllocHGlobal(capacity);
            Available = true;
        }

        if (!TryQuery()) return [];

        var now = Stopwatch.GetTimestamp();
        var elapsed = previousTimestamp == 0 ? 0 : (now - previousTimestamp) / (double)Stopwatch.Frequency;
        previousTimestamp = now;

        var cores = Environment.ProcessorCount;
        var results = new List<ProcessEntry>(320);
        var seen = new HashSet<int>(320);
        var offset = 0;

        while (true)
        {
            var record = Marshal.PtrToStructure<SystemProcessInformationBlock>(buffer + offset);
            var id = unchecked((int)record.UniqueProcessId.ToInt64());
            seen.Add(id);

            var cpuTicks = record.KernelTime + record.UserTime;
            double percent = 0;
            if (elapsed > 0 && previous.TryGetValue(id, out var before) && before.Created == record.CreateTime)
            {
                var delta = cpuTicks - before.Cpu;
                if (delta > 0) percent = delta / 10_000_000.0 / elapsed / cores * 100.0;
            }

            previous[id] = (cpuTicks, record.CreateTime);
            results.Add(new ProcessEntry(id, ReadName(record, id), Math.Clamp(percent, 0, 100),
                record.WorkingSetSize, ResolvePath(id, record.CreateTime)));

            if (record.NextEntryOffset == 0) break;
            offset += (int)record.NextEntryOffset;
            if (offset < 0 || offset >= capacity) break;
        }

        foreach (var stale in previous.Keys.Where(key => !seen.Contains(key)).ToList()) previous.Remove(stale);
        foreach (var stale in pathCache.Keys.Where(key => !seen.Contains(key)).ToList()) pathCache.Remove(stale);

        return results;
    }

    bool TryQuery()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var status = NtQuerySystemInformation(SystemProcessInformation, buffer, capacity, out var needed);
            if (status == 0) return true;

            if (status != StatusInfoLengthMismatch)
            {
                Available = false;
                return false;
            }

            Marshal.FreeHGlobal(buffer);
            capacity = Math.Max(capacity * 2, needed + 128 * 1024);
            buffer = Marshal.AllocHGlobal(capacity);
        }

        Available = false;
        return false;
    }

    static string ReadName(SystemProcessInformationBlock record, int id)
    {
        if (record.ImageNameBuffer == IntPtr.Zero || record.ImageNameLength == 0)
            return id == 0 ? "System Idle Process" : "System";
        return Marshal.PtrToStringUni(record.ImageNameBuffer, record.ImageNameLength / 2) ?? "";
    }

    string ResolvePath(int id, long created)
    {
        if (pathCache.TryGetValue(id, out var cached) && cached.Created == created) return cached.Path;

        var resolved = "";
        using (var handle = OpenProcess(QueryLimitedInformation, false, id))
        {
            if (!handle.IsInvalid)
            {
                var size = 512;
                var builder = new char[size];
                if (QueryFullProcessImageName(handle, 0, builder, ref size) && size > 0)
                    resolved = new string(builder, 0, size);
            }
        }

        pathCache[id] = (created, resolved);
        return resolved;
    }

    public void Dispose()
    {
        if (buffer == IntPtr.Zero) return;
        Marshal.FreeHGlobal(buffer);
        buffer = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SystemProcessInformationBlock
    {
        public uint NextEntryOffset;
        public uint NumberOfThreads;
        public long WorkingSetPrivateSize;
        public uint HardFaultCount;
        public uint NumberOfThreadsHighWatermark;
        public ulong CycleTime;
        public long CreateTime;
        public long UserTime;
        public long KernelTime;
        public ushort ImageNameLength;
        public ushort ImageNameMaximumLength;
        public IntPtr ImageNameBuffer;
        public int BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
        public uint HandleCount;
        public uint SessionId;
        public UIntPtr UniqueProcessKey;
        public UIntPtr PeakVirtualSize;
        public UIntPtr VirtualSize;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSizeRaw;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivatePageCount;
        public long ReadOperationCount;
        public long WriteOperationCount;
        public long OtherOperationCount;
        public long ReadTransferCount;
        public long WriteTransferCount;
        public long OtherTransferCount;

        public readonly long WorkingSetSize => (long)(ulong)WorkingSetSizeRaw;
    }

    [DllImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern uint NtQuerySystemInformation(int infoClass, IntPtr buffer, int size, out int needed);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char[] name, ref int size);
}
