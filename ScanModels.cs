using System.Runtime.InteropServices;

namespace DiskLit;

internal sealed record FileEntry(string Name, string FullPath, long Size, DateTime Modified);

internal sealed record FolderSummary(string FullPath, string Display, long TotalBytes, long TotalFiles, int Depth);

internal sealed record ScanResult(
    List<FileEntry> Largest,
    long Files,
    long Bytes,
    long InaccessibleEntries,
    TimeSpan Elapsed,
    IReadOnlyList<FolderSummary> Folders,
    bool IsSolidState);

internal sealed class ScanProgress
{
    public long Files;
    public long Bytes;
    public string Current = "";
    public long Inaccessible;
    public List<FileEntry>? Largest;
}

internal sealed class FolderIndex
{
    struct Folder
    {
        public int Parent;
        public string Name;
        public long OwnBytes;
        public long TotalBytes;
        public long OwnFiles;
        public long TotalFiles;
    }

    readonly object gate = new();
    readonly List<Folder> folders = new(4096);

    public int Register(int parent, string name)
    {
        lock (gate)
        {
            folders.Add(new Folder { Parent = parent, Name = name });
            return folders.Count - 1;
        }
    }

    public void SetOwnTotals(int index, long bytes, long files)
    {
        lock (gate)
        {
            var span = CollectionsMarshal.AsSpan(folders);
            span[index].OwnBytes = bytes;
            span[index].OwnFiles = files;
        }
    }

    public IReadOnlyList<FolderSummary> Rollup(string root, int maxDepth, int limit)
    {
        lock (gate)
        {
            var span = CollectionsMarshal.AsSpan(folders);
            for (var i = 0; i < span.Length; i++)
            {
                span[i].TotalBytes = span[i].OwnBytes;
                span[i].TotalFiles = span[i].OwnFiles;
            }

            for (var i = span.Length - 1; i > 0; i--)
            {
                var parent = span[i].Parent;
                if (parent < 0) continue;
                span[parent].TotalBytes += span[i].TotalBytes;
                span[parent].TotalFiles += span[i].TotalFiles;
            }

            var depths = new int[span.Length];
            for (var i = 0; i < span.Length; i++)
            {
                var parent = span[i].Parent;
                depths[i] = parent < 0 ? 0 : depths[parent] + 1;
            }

            var candidates = new List<(int Index, long Bytes)>();
            for (var i = 0; i < span.Length; i++)
                if (depths[i] >= 1 && depths[i] <= maxDepth)
                    candidates.Add((i, span[i].TotalBytes));

            candidates.Sort((left, right) => right.Bytes.CompareTo(left.Bytes));

            var results = new List<FolderSummary>(Math.Min(limit, candidates.Count));
            foreach (var (index, _) in candidates.Take(limit))
            {
                var relative = BuildPath(span, index);
                results.Add(new FolderSummary(
                    Path.Join(root, relative),
                    relative,
                    span[index].TotalBytes,
                    span[index].TotalFiles,
                    depths[index]));
            }

            return results;
        }
    }

    static string BuildPath(Span<Folder> span, int index)
    {
        var segments = new Stack<string>();
        for (var current = index; current > 0; current = span[current].Parent)
            segments.Push(span[current].Name);
        return string.Join(Path.DirectorySeparatorChar, segments);
    }
}
