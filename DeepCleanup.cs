using Microsoft.Win32;

namespace DiskLit;

internal static class DeepCleanup
{
    const long MinimumBytes = 100L * 1024 * 1024;
    static readonly TimeSpan MinimumAge = TimeSpan.FromDays(45);
    static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Packages", "Temp", "Comms", "ConnectedDevicesPlatform", "CrashDumps",
        "Application Data", "History", "Temporary Internet Files"
    };
    public static IEnumerable<CleanupGroup> Find(CancellationToken token)
    {
        var inventory = CaptureInventory();
        foreach (var root in Roots())
        {
            if (!Directory.Exists(root)) continue;
            var items = new List<CleanupItem>();
            foreach (var directory in Directories(root))
            {
                token.ThrowIfCancellationRequested();
                if (!Eligible(directory, inventory)) continue;
                var fingerprint = CleanupInspection.Inspect(directory, token);
                if (fingerprint is null || fingerprint.Size < MinimumBytes
                    || DateTime.UtcNow - fingerprint.NewestWriteUtc < MinimumAge) continue;
                items.Add(new CleanupItem(directory, fingerprint.Size, true, root, fingerprint, IsDeep: true));
            }

            if (items.Count > 0)
                yield return new CleanupGroup(UiText.TargetOrphanedApps, root, items,
                    items.Sum(item => item.Size), Recommended: false);
        }
    }

    public static bool IsApprovedRoot(string root) => Roots().Any(candidate =>
        string.Equals(Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));

    internal sealed record Inventory(HashSet<string> Names, IReadOnlyList<string> Paths, bool Complete);

    internal static Inventory CaptureInventory()
    {
        var names = InstalledNames(out var complete, out var paths);
        names.UnionWith(ActiveNames());
        using var monitor = new ProcessMonitor();
        foreach (var process in monitor.Sample())
            if (!string.IsNullOrEmpty(process.ExecutablePath)) paths.Add(process.ExecutablePath);
        return new Inventory(names, paths, complete && monitor.Available);
    }

    internal static bool Eligible(string path, Inventory inventory)
    {
        var name = Path.GetFileName(path);
        return inventory.Complete && !Protected.Contains(name)
            && !IsInstalled(name, inventory.Names)
            && KnownDataCatalog.Match(path) is null
            && !KnownDataCatalog.Rules.Any(rule => Cleanup.IsInside(rule.Root, path))
            && !inventory.Paths.Any(known => string.Equals(path, known, StringComparison.OrdinalIgnoreCase)
                || Cleanup.IsInside(path, known) || Cleanup.IsInside(known, path));
    }
    static IEnumerable<string> Roots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    static string[] Directories(string root)
    {
        try { return CleanupInspection.SafePath(root) ? Directory.GetDirectories(root) : []; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }

    static HashSet<string> ActiveNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                var normalized = Normalize(process.ProcessName);
                if (normalized.Length >= 4) names.Add(normalized);
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
        return names;
    }

    static bool IsInstalled(string directoryName, HashSet<string> installed)
    {
        var candidate = Normalize(directoryName);
        if (candidate.Length < 4) return true;
        return installed.Any(name => name.Contains(candidate, StringComparison.Ordinal) || candidate.Contains(name, StringComparison.Ordinal));
    }

    static HashSet<string> InstalledNames(out bool complete, out List<string> paths)
    {
        complete = true;
        paths = [];
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var keyName in uninstall.GetSubKeyNames())
                {
                    using var key = uninstall.OpenSubKey(keyName);
                    Add(key?.GetValue("DisplayName") as string);
                    var location = key?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(location) && Path.IsPathFullyQualified(location))
                    {
                        paths.Add(Path.GetFullPath(location).TrimEnd(Path.DirectorySeparatorChar));
                        Add(Path.GetFileName(location.TrimEnd(Path.DirectorySeparatorChar)));
                    }
                }
            }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException) { complete = false; }
        }
        return names;

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var normalized = Normalize(value);
            if (normalized.Length >= 4) names.Add(normalized);
        }
    }

    static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
