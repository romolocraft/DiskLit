using System.Runtime.InteropServices;

namespace DiskLit;

internal sealed record CleanupItem(string FullPath, long Size, bool IsDirectory);

internal sealed record CleanupGroup(UiText Name, string Root, IReadOnlyList<CleanupItem> Items, long TotalBytes);

internal sealed record CleanupOutcome(int Removed, int Skipped, long BytesRemoved);

internal static class Cleanup
{
    sealed record Target(UiText Name, string Root, string Pattern);

    public static IReadOnlyList<CleanupGroup> BuildPlan(CancellationToken token)
    {
        var groups = new List<CleanupGroup>();
        foreach (var target in Targets())
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(target.Root) || !Directory.Exists(target.Root)) continue;

            var items = new List<CleanupItem>();
            long total = 0;

            foreach (var entry in SafeEntries(target.Root, target.Pattern))
            {
                token.ThrowIfCancellationRequested();
                if (!IsInside(entry.Path, target.Root)) continue;
                var size = entry.IsDirectory ? DirectorySize(entry.Path, token) : FileSize(entry.Path);
                if (size < 0) continue;
                items.Add(new CleanupItem(entry.Path, size, entry.IsDirectory));
                total += size;
            }

            if (items.Count > 0) groups.Add(new CleanupGroup(target.Name, target.Root, items, total));
        }

        return groups;
    }

    public static CleanupOutcome Execute(IReadOnlyList<CleanupItem> items)
    {
        var allowed = Targets()
            .Select(target => target.Root)
            .Where(root => !string.IsNullOrEmpty(root))
            .ToList();

        var verified = items
            .Where(item => allowed.Any(root => IsInside(item.FullPath, root)))
            .ToList();

        if (verified.Count == 0) return new CleanupOutcome(0, items.Count, 0);

        var paths = verified.Select(item => item.FullPath).ToList();
        if (!OnStaThread(() => Recycle(paths)))
            return new CleanupOutcome(0, items.Count, 0);

        var removed = 0;
        long bytes = 0;
        foreach (var item in verified)
        {
            if (StillPresent(item)) continue;
            removed++;
            bytes += item.Size;
        }

        return new CleanupOutcome(removed, items.Count - removed, bytes);
    }

    static bool StillPresent(CleanupItem item) =>
        item.IsDirectory ? Directory.Exists(item.FullPath) : File.Exists(item.FullPath);

    static bool OnStaThread(Func<bool> action)
    {
        var result = false;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception error) when (error is ExternalException or InvalidOperationException or UnauthorizedAccessException)
            {
                result = false;
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    public static bool RecycleChosen(string path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && OnStaThread(() => Recycle([path]));

    static IEnumerable<Target> Targets()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        yield return new Target(UiText.TargetUserTemp, Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), "*");
        yield return new Target(UiText.TargetWindowsTemp, Path.Join(windows, "Temp"), "*");
        yield return new Target(UiText.TargetUpdateCache, Path.Join(windows, "SoftwareDistribution", "Download"), "*");
        yield return new Target(UiText.TargetThumbnails, Path.Join(local, "Microsoft", "Windows", "Explorer"), "thumbcache_*.db");
        yield return new Target(UiText.TargetCrashDumps, Path.Join(local, "CrashDumps"), "*");
        yield return new Target(UiText.TargetDeliveryOptimization,
            Path.Join(windows, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization"), "*");

        foreach (var browser in BrowserCaches(local))
            yield return new Target(UiText.TargetBrowserCache, browser, "*");
    }

    static IEnumerable<string> BrowserCaches(string local)
    {
        yield return Path.Join(local, "Microsoft", "Edge", "User Data", "Default", "Cache", "Cache_Data");
        yield return Path.Join(local, "Google", "Chrome", "User Data", "Default", "Cache", "Cache_Data");

        var firefox = Path.Join(local, "Mozilla", "Firefox", "Profiles");
        if (!Directory.Exists(firefox)) yield break;

        string[] profiles;
        try
        {
            profiles = Directory.GetDirectories(firefox);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var profile in profiles) yield return Path.Join(profile, "cache2", "entries");
    }

    static IEnumerable<(string Path, bool IsDirectory)> SafeEntries(string root, string pattern)
    {
        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(root, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
            yield return (entry, (attributes & FileAttributes.Directory) != 0);
        }
    }

    static long FileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    static long DirectorySize(string root, CancellationToken token)
    {
        long total = 0;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            foreach (var (path, isDirectory) in SafeEntries(directory, "*"))
            {
                if (isDirectory)
                {
                    pending.Push(path);
                    continue;
                }

                var size = FileSize(path);
                if (size > 0) total += size;
            }
        }

        return total;
    }

    static bool IsInside(string candidate, string root)
    {
        try
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            var normalized = Path.GetFullPath(candidate);
            return normalized.Length > normalizedRoot.Length
                && normalized.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                && normalized[normalizedRoot.Length] == Path.DirectorySeparatorChar;
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    static bool Recycle(IEnumerable<string> paths)
    {
        var joined = string.Join('\0', paths) + "\0\0";
        var operation = new ShellFileOperation
        {
            Function = FoDelete,
            From = joined,
            Flags = FofAllowUndo | FofNoConfirmation | FofWantNukeWarning | FofNoErrorUi | FofNoConfirmMkDir | FofSilent
        };

        return ShFileOperation(ref operation) == 0 && !operation.Aborted;
    }

    const uint FoDelete = 3;
    const ushort FofAllowUndo = 0x0040;
    const ushort FofNoConfirmation = 0x0010;
    const ushort FofNoErrorUi = 0x0400;
    const ushort FofNoConfirmMkDir = 0x0200;
    const ushort FofSilent = 0x0004;
    const ushort FofWantNukeWarning = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShellFileOperation
    {
        public IntPtr Window;
        public uint Function;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        [MarshalAs(UnmanagedType.LPWStr)] public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Aborted;
        public IntPtr NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int ShFileOperation(ref ShellFileOperation operation);
}
