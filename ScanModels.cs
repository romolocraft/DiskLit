namespace DiskLit;

internal sealed record FileEntry(string Name, string FullPath, long Size, DateTime Modified);

internal sealed record ScanResult(
    List<FileEntry> Largest,
    long Files,
    long Bytes,
    long InaccessibleEntries,
    TimeSpan Elapsed,
    IReadOnlyDictionary<string, long> Categories,
    bool IsSolidState);

internal sealed class ScanProgress
{
    public long Files;
    public long Bytes;
    public string Current = "";
    public long Inaccessible;
    public List<FileEntry>? Largest;
}
