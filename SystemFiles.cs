namespace DiskLit;

internal static class SystemFiles
{
    static readonly (string Name, UiText Explanation)[] ByFileName =
    [
        ("hiberfil.sys", UiText.SystemFileHiberfil),
        ("pagefile.sys", UiText.SystemFilePagefile),
        ("swapfile.sys", UiText.SystemFileSwapfile),
        ("memory.dmp", UiText.SystemFileMemoryDump),
        ("dumpstack.log", UiText.SystemFileDumpStack),
        ("dumpstack.log.tmp", UiText.SystemFileDumpStack)
    ];

    static readonly (string Segment, UiText Explanation)[] BySegment =
    [
        ("$Recycle.Bin", UiText.SystemFileRecycleBin),
        ("Windows.old", UiText.SystemFileWindowsOld),
        ("WinSxS", UiText.SystemFileWinSxS),
        ("Installer", UiText.SystemFileInstaller),
        ("System Volume Information", UiText.SystemFileSystemVolume)
    ];

    public static UiText? Describe(string fullPath)
    {
        var name = Path.GetFileName(fullPath.AsSpan());
        foreach (var (candidate, explanation) in ByFileName)
            if (name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return explanation;

        if (name.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase)
            && Contains(fullPath, "CrashDumps"))
            return UiText.SystemFileMemoryDump;

        foreach (var (segment, explanation) in BySegment)
        {
            if (!Contains(fullPath, segment)) continue;
            if (segment == "Installer" && !Contains(fullPath, "Windows")) continue;
            return explanation;
        }

        return null;
    }

    public static bool IsSystemItem(string fullPath) => Describe(fullPath) is not null;

    static bool Contains(string path, string segment)
    {
        var index = path.IndexOf(segment, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var beforeOk = index == 0 || path[index - 1] == Path.DirectorySeparatorChar;
            var after = index + segment.Length;
            var afterOk = after == path.Length || path[after] == Path.DirectorySeparatorChar;
            if (beforeOk && afterOk) return true;
            index = path.IndexOf(segment, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
