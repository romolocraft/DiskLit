using System.Runtime.InteropServices;

namespace DiskLit;

internal sealed record CleanupFingerprint(long Size, long Files, DateTime NewestWriteUtc, string MetadataHash);

internal sealed record CleanupItem(string FullPath, long Size, bool IsDirectory, string SourceRoot,
    CleanupFingerprint? Fingerprint = null, bool IsDeep = false);

internal sealed record CleanupGroup(UiText Name, string Root, IReadOnlyList<CleanupItem> Items, long TotalBytes, bool Recommended = true);

internal sealed record CleanupOutcome(int Removed, int Skipped, long BytesRemoved, bool Cancelled = false, bool Failed = false);

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
                var fingerprint = CleanupInspection.Inspect(entry.Path, token);
                if (fingerprint is null) continue;
                items.Add(new CleanupItem(entry.Path, fingerprint.Size, entry.IsDirectory, target.Root, fingerprint));
                total += fingerprint.Size;
            }

            if (items.Count > 0) groups.Add(new CleanupGroup(target.Name, target.Root, items, total));
        }

        groups.AddRange(DeepCleanup.Find(token));
        return groups;
    }

    public static CleanupOutcome Execute(IReadOnlyList<CleanupItem> items, CancellationToken token = default)
    {
        CleanupOutcome outcome = new(0, items.Count, 0, Cancelled: true);
        OnStaThread(() =>
        {
            if (!ValidateBatch(items, token)) return false;
            var result = RecycleBin.Move(items, () => ValidateBatch(items, token), token);
            outcome = new CleanupOutcome(result.Removed, items.Count - result.Removed,
                result.Bytes, Cancelled: !result.Started, Failed: result.Started && !result.Success);
            return result.Success;
        });
        return outcome;
    }

    internal static bool ValidateBatch(IReadOnlyList<CleanupItem> items, CancellationToken token = default)
    {
        try
        {
            if (items.Count == 0 || token.IsCancellationRequested) return false;
            var roots = Targets().ToArray();
            var inventory = items.Any(item => item.IsDeep) ? DeepCleanup.CaptureInventory() : null;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = items.Select(item => Path.GetFullPath(item.FullPath)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            for (var index = 1; index < ordered.Length; index++)
                if (IsInside(ordered[index], ordered[index - 1])) return false;
            foreach (var item in items)
            {
                if (token.IsCancellationRequested || !IsInside(item.FullPath, item.SourceRoot)
                    || !paths.Add(Path.GetFullPath(item.FullPath))) return false;
                if (item.IsDeep)
                {
                    if (!DeepCleanup.IsApprovedRoot(item.SourceRoot) || inventory is null
                        || !string.Equals(Path.GetDirectoryName(item.FullPath), item.SourceRoot, StringComparison.OrdinalIgnoreCase)
                        || !DeepCleanup.Eligible(item.FullPath, inventory)) return false;
                }
                else if (!roots.Any(target => string.Equals(target.Root, item.SourceRoot, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetDirectoryName(item.FullPath), target.Root, StringComparison.OrdinalIgnoreCase)
                    && System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(target.Pattern, Path.GetFileName(item.FullPath))))
                    return false;
                if (item.Fingerprint is null || item.Fingerprint.Size != item.Size
                    || ((File.GetAttributes(item.FullPath) & FileAttributes.Directory) != 0) != item.IsDirectory
                    || CleanupInspection.Inspect(item.FullPath, token, exclusive: true) != item.Fingerprint) return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or OperationCanceledException or System.Security.SecurityException) { return false; }
    }
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

    public static bool RecycleChosen(string path)
    {
        var snapshot = CleanupInspection.Inspect(path, CancellationToken.None, exclusive: true, protectPersonalData: false);
        if (snapshot is null) return false;
        var item = new CleanupItem(path, snapshot.Size, Directory.Exists(path), Path.GetDirectoryName(path)!, snapshot);
        return OnStaThread(() => RecycleBin.Move([item],
            () => CleanupInspection.Inspect(path, CancellationToken.None, exclusive: true, protectPersonalData: false) == snapshot,
            CancellationToken.None).Success);
    }
    static IEnumerable<Target> Targets()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        yield return new Target(UiText.TargetUserTemp, Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), "*");
        yield return new Target(UiText.TargetWindowsTemp, Path.Join(windows, "Temp"), "*");

        yield return new Target(UiText.TargetThumbnails, Path.Join(local, "Microsoft", "Windows", "Explorer"), "thumbcache_*.db");
        yield return new Target(UiText.TargetCrashDumps, Path.Join(local, "CrashDumps"), "*");

        foreach (var rule in KnownDataCatalog.Rules.Where(rule => rule.OfferCleanup))
            yield return new Target(UiText.TargetKnownCache, rule.Root, "*");
    }

    static IEnumerable<(string Path, bool IsDirectory)> SafeEntries(string root, string pattern)
    {
        string[] entries;
        try
        {
            if (!CleanupInspection.SafePath(root)) yield break;
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

    internal static bool IsInside(string candidate, string root)
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

}
