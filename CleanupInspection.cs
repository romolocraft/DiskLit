using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DiskLit;

internal static class CleanupInspection
{
    public static CleanupFingerprint? Inspect(string root, CancellationToken token, bool exclusive = false,
        bool protectPersonalData = true)
    {
        try
        {
            if (!SafePath(root)) return null;
            long bytes = 0, files = 0;
            var newest = DateTime.MinValue;
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var pending = new Stack<string>();
            pending.Push(Path.GetFullPath(root));
            while (pending.TryPop(out var path))
            {
                token.ThrowIfCancellationRequested();
                if (!SafePath(path)) return null;
                if (protectPersonalData && KnownDataCatalog.MustPreserve(path)) return null;
                NativeInfo info;
                using (var handle = CreateFile(path, exclusive ? 0x80010000u : 0x80u,
                    exclusive ? 0u : 7u, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
                {
                    if (handle.IsInvalid || !GetFileInformationByHandle(handle, out info)) return null;
                }
                if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) return null;
                var directory = (info.Attributes & FileAttributes.Directory) != 0;
                var length = directory ? 0 : ((long)info.SizeHigh << 32) | info.SizeLow;
                bytes = checked(bytes + length);
                if (!directory) files++;
                var modified = DateTime.FromFileTimeUtc(info.Write);
                if (modified > newest) newest = modified;
                using var stream = new MemoryStream();
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(Path.GetRelativePath(root, path));
                    writer.Write((uint)info.Attributes);
                    writer.Write(length);
                    writer.Write(info.Creation);
                    writer.Write(info.Write);
                    writer.Write(info.Volume);
                    writer.Write(info.IndexHigh);
                    writer.Write(info.IndexLow);
                }
                digest.AppendData(stream.GetBuffer().AsSpan(0, (int)stream.Length));
                if (!directory) continue;
                var children = Directory.GetFileSystemEntries(path);
                Array.Sort(children, StringComparer.Ordinal);
                foreach (var child in children) pending.Push(child);
            }
            return new CleanupFingerprint(bytes, files, newest, Convert.ToHexString(digest.GetHashAndReset()));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException or OverflowException) { return null; }
    }

    public static bool SafePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.AsSpan(2).Contains(':')) return false;
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        foreach (var segment in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.EndsWith(' ') || segment.EndsWith('.')) return false;
            current = Path.Join(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        }
        return true;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct NativeInfo
    {
        public FileAttributes Attributes;
        public long Creation;
        public long Access;
        public long Write;
        public uint Volume;
        public uint SizeHigh;
        public uint SizeLow;
        public uint Links;
        public uint IndexHigh;
        public uint IndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle file, out NativeInfo info);
}
