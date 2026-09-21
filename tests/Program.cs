using DiskLit;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace DiskLit.Tests;

internal static class Program
{
    static int passed;
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--visual")) { Visual(); return; }
        var root = Path.Join(Path.GetTempPath(), "DiskLit-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Console.WriteLine("Synthetic fixtures retained: " + root);
        var first = Path.Join(root, "first.bin");
        var second = Path.Join(root, "second.bin");
        File.WriteAllText(first, "sample");
        File.WriteAllText(second, "other sample");
        var snapshot = CleanupInspection.Inspect(root, CancellationToken.None)!;
        Check(snapshot is { Files: 2, Size: 18 }, "native metadata size and count");
        Check(snapshot == CleanupInspection.Inspect(root, CancellationToken.None, exclusive: true), "unchanged exclusive snapshot");
        var item = new CleanupItem(root, snapshot.Size, true, Path.GetTempPath().TrimEnd('\\'), snapshot);
        Check(Cleanup.ValidateBatch([item]), "valid batch preflight only");
        Check(!Cleanup.ValidateBatch([item, item]), "duplicate rejected");
        Check(!Cleanup.ValidateBatch([item with { Fingerprint = null }]), "missing fingerprint rejected");
        Check(!Cleanup.ValidateBatch([item with { FullPath = root + "-missing" }]), "all-invalid batch rejected");
        Check(!Cleanup.ValidateBatch([item, item with { FullPath = root + "-missing" }]), "one invalid item rejects whole selection");
        Check(!Cleanup.ValidateBatch([item], new CancellationToken(true)), "cancelled preflight");
        using (File.Open(first, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            Check(!Cleanup.ValidateBatch([item]), "open file cancels even with permissive sharing");
        using (var handle = CreateFile(root, 0x80000000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
        {
            Check(!handle.IsInvalid, "directory lock fixture");
            Check(!Cleanup.ValidateBatch([item]), "open directory cancels batch");
        }
        var modified = File.GetLastWriteTimeUtc(first);
        File.SetLastWriteTimeUtc(first, modified.AddMinutes(-1));
        Check(!Cleanup.ValidateBatch([item]), "older non-maximum timestamp divergence");
        File.SetLastWriteTimeUtc(first, modified);
        File.Move(first, Path.Join(root, "renamed.bin"));
        Check(!Cleanup.ValidateBatch([item]), "rename with same size and count rejected");
        Check(CleanupInspection.Inspect(Path.Join(root, "missing"), CancellationToken.None) is null, "missing path fails closed");
        Directory.CreateDirectory(Path.Join(root, "mods"));
        Check(CleanupInspection.Inspect(root, CancellationToken.None) is null, "empty personal directory protects whole candidate");
        var blocked = Directory.CreateDirectory(Path.Join(root, "blocked"));
        var originalAcl = blocked.GetAccessControl();
        var restricted = blocked.GetAccessControl();
        restricted.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.ListDirectory, AccessControlType.Deny));
        try
        {
            blocked.SetAccessControl(restricted);
            Check(CleanupInspection.Inspect(blocked.FullName, CancellationToken.None) is null, "inaccessible directory is not an empty successful snapshot");
        }
        finally { blocked.SetAccessControl(originalAcl); }
        var junctionIndex = Array.IndexOf(args, "--junction");
        if (junctionIndex >= 0)
        {
            var junction = args[junctionIndex + 1];
            Check(CleanupInspection.Inspect(junction, CancellationToken.None) is null, "junction root rejected");
            Check(CleanupInspection.Inspect(Path.Join(junction, "child.bin"), CancellationToken.None) is null, "junction ancestor rejected");
            Check(CleanupInspection.Inspect(Path.GetDirectoryName(junction)!, CancellationToken.None) is null, "nested junction rejects entire candidate");
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Check(KnownDataCatalog.Match(Path.Join(local, @"Google\Chrome\User Data\Default\Cache\data.bin"))?.Recreatable == true, "specific Chrome cache");
        Check(KnownDataCatalog.MustPreserve(Path.Join(local, @"Google\Chrome\User Data\Default\Bookmarks")), "Chrome bookmarks preserved");
        Check(KnownDataCatalog.MustPreserve(Path.Join(local, @"Google\Chrome\User Data\Default\Cache\saves\a.sav")), "personal data overrides cache");
        Check(KnownDataCatalog.Match(Path.Join(root, "NVIDIA", "data.bin")) is null, "brand alone is not a cache rule");
        Check(KnownDataCatalog.Match(Path.Join(local, @"NVIDIA\DXCache\x"))?.Kind == DataKind.GpuCache, "GPU cache rule");
        Check(KnownDataCatalog.Match(Path.Join(local, @"NVIDIA\DXCacheBackup\x")) is null, "path boundary");
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Check(KnownDataCatalog.Match(Path.Join(windows, @"System32\DriverStore\x"))?.Kind == DataKind.DriverStore, "specific rule precedes broad system rule");
        Check(!DeepCleanup.Eligible(root, new([], [], false)), "incomplete inventory rejected");
        Check(!DeepCleanup.Eligible(root, new([], [Path.Join(root, "app.exe")], true)), "active executable path protects candidate");
        foreach (var language in Enum.GetValues<Language>())
        {
            Strings.Use(language);
            foreach (var text in Enum.GetValues<UiText>()) Check(!string.IsNullOrWhiteSpace(Strings.Get(text)), $"{language}/{text}");
        }
        var sink = new RecycleProgressSink([item], CancellationToken.None);
        Check(sink.PreDeleteItem(0, IntPtr.Zero) < 0, "permanent deletion vetoed without touching Shell");
        Check(sink.PostDeleteItem(0x80, IntPtr.Zero, 0, IntPtr.Zero) < 0, "no false success without recycle item");
        Console.WriteLine($"PASS: {passed} assertions. No cleanup or Shell operations executed.");
    }

    static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("FAILED: " + label);
        passed++;
    }

    static void Visual()
    {
        Strings.Use(Language.Portuguese);
        Theme.Use(ThemeMode.Dark);
        using var form = new Form { Text = "DiskLit — synthetic visual smoke", Size = new(1100, 720), AutoScaleMode = AutoScaleMode.Dpi };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var files = new FilesPage();
        var active = new ActivePage();
        var cleanup = new CleanupPage();
        foreach (var (name, page) in new (string, Page)[] { ("Arquivos", files), ("Ativos", active), ("Limpeza", cleanup) })
        {
            var tab = new TabPage(name); tab.Controls.Add(page); tabs.TabPages.Add(tab);
            page.Visible = true; page.ApplyLanguage(); page.ApplyTheme(Theme.Current);
        }
        var entries = Enumerable.Range(0, 10000).Select(i => new FileEntry($"Fixture {i:D5}.bin", $@"E:\Synthetic\Folder{i / 100}\Fixture {i:D5}.bin", 10000 - i, DateTime.Now)).ToList();
        Set(files, "allFiles", entries);
        Invoke(files, "ApplyFilter");
        var folders = Enumerable.Range(0, 100).Select(i => new FolderSummary($@"E:\Synthetic\Folder{i}", $"Folder {i}", 1000, 20, 1)).ToArray();
        Invoke(files, "RefreshFolderChoices", (object)folders);
        var items = Enumerable.Range(0, 100).Select(i => new CleanupItem($@"E:\Synthetic\Candidate{i}", 100, true, @"E:\Synthetic", IsDeep: true)).ToArray();
        Set(cleanup, "groups", new CleanupGroup[] { new(UiText.TargetOrphanedApps, @"E:\Synthetic", items, 10000, false) });
        Invoke(cleanup, "Populate");
        tabs.SelectedIndexChanged += (_, _) => { active.Deactivated(); if (tabs.SelectedIndex == 1) active.Activated(); };
        var toggle = new Button { Text = "Claro / escuro", Dock = DockStyle.Bottom };
        toggle.Click += (_, _) => { Theme.Use(Theme.IsDark ? ThemeMode.Light : ThemeMode.Dark); foreach (var page in new Page[] { files, active, cleanup }) page.ApplyTheme(Theme.Current); };
        form.Controls.Add(tabs); form.Controls.Add(toggle);
        form.FormClosing += (_, _) => { foreach (var page in new Page[] { files, active, cleanup }) page.CancelWork(); };
        Application.Run(form);
    }

    static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
}
