using System.Runtime.InteropServices;

namespace DiskLit;

internal static class Icons
{
    const uint ShgfiIcon = 0x000000100;
    const uint ShgfiSmallIcon = 0x000000001;
    const uint ShgfiLargeIcon = 0x000000000;
    const uint ShgfiUseFileAttributes = 0x000000010;
    const uint FileAttributeNormal = 0x00000080;
    const uint FileAttributeDirectory = 0x00000010;

    static readonly Dictionary<string, Image?> byExtension = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, Image?> byPath = new(StringComparer.OrdinalIgnoreCase);

    public static Image? ForFileName(string name)
    {
        var dot = name.LastIndexOf('.');
        var extension = dot < 0 ? "" : name[dot..];
        if (byExtension.TryGetValue(extension, out var cached)) return cached;

        var image = Extract("placeholder" + extension, FileAttributeNormal, ShgfiUseFileAttributes | ShgfiSmallIcon);
        byExtension[extension] = image;
        return image;
    }

    public static Image? ForFolder()
    {
        if (byExtension.TryGetValue("<dir>", out var cached)) return cached;
        var image = Extract("placeholder", FileAttributeDirectory, ShgfiUseFileAttributes | ShgfiSmallIcon);
        byExtension["<dir>"] = image;
        return image;
    }

    public static Image? ForExecutable(string path, bool large = false)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var key = large ? path + "*" : path;
        if (byPath.TryGetValue(key, out var cached)) return cached;

        var image = Extract(path, FileAttributeNormal, large ? ShgfiLargeIcon : ShgfiSmallIcon);
        byPath[key] = image;
        return image;
    }

    static Bitmap? Extract(string path, uint attributes, uint flags)
    {
        var info = new ShellFileInfo();
        var result = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), ShgfiIcon | flags);
        if (result == IntPtr.Zero || info.Icon == IntPtr.Zero) return null;

        try
        {
            using var icon = Icon.FromHandle(info.Icon);
            return icon.ToBitmap();
        }
        catch (Exception error) when (error is ArgumentException or OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            DestroyIcon(info.Icon);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(IntPtr icon);
}
