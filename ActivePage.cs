using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace DiskLit;

internal sealed class ActivePage : Page
{
    const int RefreshIntervalMilliseconds = 1_500;

    readonly ProcessMonitor monitor = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = RefreshIntervalMilliseconds };
    readonly Card card = new();
    readonly ThemedListView list = DetailList(virtualMode: true);
    readonly ImageList icons = new() { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(16, 16) };
    readonly Dictionary<string, int> iconIndex = new(StringComparer.OrdinalIgnoreCase);
    readonly Label hint = new() { AutoSize = false, Dock = DockStyle.Top };
    readonly Panel statusBar = new();
    readonly Label summary = new() { AutoEllipsis = true, Dock = DockStyle.Fill };
    readonly Label versionLabel = new() { AutoSize = true, Dock = DockStyle.Right };
    readonly ContextMenuStrip menu = new();

    List<Row> rows = [];
    double totalCpu;
    long totalMemory;

    sealed record Row(ProcessEntry? Process, UiText? Header);

    public ActivePage()
    {
        hint.Height = LogicalToDeviceUnits(34);

        list.SmallImageList = icons;
        list.HideHorizontalScrollBar = true;
        list.SectionAt = index => index >= 0 && index < rows.Count && rows[index].Header is { } header
            ? Strings.Get(header)
            : null;
        list.Columns.Add("", LogicalToDeviceUnits(230));
        list.Columns.Add("", LogicalToDeviceUnits(85));
        list.Columns.Add("", LogicalToDeviceUnits(65), HorizontalAlignment.Right);
        list.Columns.Add("", LogicalToDeviceUnits(90), HorizontalAlignment.Right);
        list.Columns.Add("", LogicalToDeviceUnits(150), HorizontalAlignment.Right);
        list.Columns.Add("", LogicalToDeviceUnits(320));
        list.RetrieveVirtualItem += (_, e) => e.Item = MakeItem(rows[e.ItemIndex]);
        list.Resize += (_, _) => ResizeColumns();
        list.ContextMenuStrip = menu;

        menu.Items.Add(new ToolStripMenuItem("", null, (_, _) => SearchSelected()));
        menu.Items.Add(new ToolStripMenuItem("", null, (_, _) => OpenSelectedLocation()));
        menu.Items.Add(new ToolStripMenuItem("", null, (_, _) => CopySelectedPath()));

        timer.Tick += (_, _) => Refresh(publish: true);

        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(14, 12, 14, 12);
        card.Controls.Add(list);

        versionLabel.Font = new Font("Segoe UI", 7.5f);
        versionLabel.TextAlign = ContentAlignment.MiddleRight;
        versionLabel.Text = "v" + Humanize.Version;
        statusBar.Dock = DockStyle.Bottom;
        statusBar.Height = LogicalToDeviceUnits(26);
        statusBar.Padding = new Padding(2, 6, 2, 0);
        statusBar.Controls.Add(summary);
        statusBar.Controls.Add(versionLabel);

        Controls.Add(card);
        Controls.Add(statusBar);
        Controls.Add(hint);
    }

    public override void Activated()
    {
        Refresh(publish: false);
        Refresh(publish: true);
        timer.Start();
    }

    public override void Deactivated()
    {
        timer.Stop();
        rows = [];
        totalCpu = 0;
        totalMemory = 0;
        if (list.IsHandleCreated) list.VirtualListSize = 0;
        icons.Images.Clear();
        iconIndex.Clear();
        monitor.Release();
    }

    public override void ApplyLanguage()
    {
        hint.Text = Strings.Get(UiText.ActiveHint);
        menu.Items[0].Text = Strings.Get(UiText.SearchProcess);
        menu.Items[1].Text = Strings.Get(UiText.OpenLocation);
        menu.Items[2].Text = Strings.Get(UiText.CopyPath);
        UpdateColumnHeaders();
        UpdateSummary();
        list.Invalidate();
    }

    public override void ApplyTheme(Palette palette)
    {
        BackColor = palette.Background;
        hint.BackColor = palette.Background;
        hint.ForeColor = palette.MutedText;
        statusBar.BackColor = palette.Background;
        summary.BackColor = palette.Background;
        summary.ForeColor = palette.MutedText;
        versionLabel.BackColor = palette.Background;
        versionLabel.ForeColor = Color.FromArgb(120, palette.MutedText);
        card.Fill = palette.Surface;
        card.Edge = palette.Border;
        list.BackColor = palette.Surface;
        list.ForeColor = palette.Text;
        if (list.IsHandleCreated) Theme.ApplyNativeListStyle(list);
        list.Invalidate();
    }

    void UpdateColumnHeaders()
    {
        list.Columns[0].Text = Strings.Get(UiText.ColumnProcess);
        list.Columns[1].Text = Strings.Get(UiText.ColumnOrigin);
        list.Columns[2].Text = Strings.Get(UiText.ColumnPid);
        list.Columns[3].Text = totalCpu > 0
            ? string.Create(CultureInfo.CurrentCulture, $"{totalCpu:0}%  {Strings.Get(UiText.ColumnCpu)}")
            : Strings.Get(UiText.ColumnCpu);
        list.Columns[4].Text = totalMemory > 0
            ? $"{Humanize.Bytes(totalMemory)}  {Strings.Get(UiText.ColumnMemory)}"
            : Strings.Get(UiText.ColumnMemory);
        list.Columns[5].Text = Strings.Get(UiText.ColumnProcessPath);
    }

    static bool IsWindowsProvided(string path)
    {
        if (path.Length == 0) return false;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length == 0) return false;
        foreach (var folder in new[] { "System32", "SysWOW64", "WinSxS", "SystemApps" })
            if (path.StartsWith(Path.Join(windows, folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        return path.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && path.LastIndexOf(Path.DirectorySeparatorChar) == windows.Length;
    }

    ProcessEntry? SelectedProcess()
    {
        if (list.SelectedIndices.Count == 0) return null;
        var index = list.SelectedIndices[0];
        return index >= 0 && index < rows.Count ? rows[index].Process : null;
    }

    void SearchSelected()
    {
        var process = SelectedProcess();
        if (process is null || process.Name.Length == 0) return;

        var query = $"{process.Name} {Strings.Get(UiText.SearchKeywords)}";
        if (Browsers.Search(query)) return;

        MessageBox.Show(Strings.Get(UiText.CouldNotOpenTitle), Strings.Get(UiText.CouldNotOpenTitle),
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    void OpenSelectedLocation()
    {
        var process = SelectedProcess();
        if (process is null || process.ExecutablePath.Length == 0) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{process.ExecutablePath}\"") { UseShellExecute = true });
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(error.Message, Strings.Get(UiText.CouldNotOpenTitle), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void CopySelectedPath()
    {
        var process = SelectedProcess();
        if (process is null || process.ExecutablePath.Length == 0) return;
        try
        {
            Clipboard.SetText(process.ExecutablePath);
        }
        catch (System.Runtime.InteropServices.ExternalException error)
        {
            MessageBox.Show(error.Message, Strings.Get(UiText.CouldNotCopyTitle), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void Refresh(bool publish)
    {
        var sample = monitor.Sample();
        if (!monitor.Available)
        {
            timer.Stop();
            summary.Text = Strings.Get(UiText.ActiveUnavailable);
            return;
        }

        if (!publish) return;

        var selectedId = SelectedProcessId();

        var apps = new List<ProcessEntry>();
        var background = new List<ProcessEntry>();
        foreach (var process in sample)
        {
            if (process.Id == 0) continue;
            (IsApp(process) ? apps : background).Add(process);
        }

        apps.Sort(Compare);
        background.Sort(Compare);

        var built = new List<Row>(sample.Count + 2);
        if (apps.Count > 0)
        {
            built.Add(new Row(null, UiText.GroupApps));
            built.AddRange(apps.Select(p => new Row(p, null)));
        }

        if (background.Count > 0)
        {
            built.Add(new Row(null, UiText.GroupBackground));
            built.AddRange(background.Select(p => new Row(p, null)));
        }

        var countChanged = built.Count != rows.Count;
        rows = built;
        totalCpu = sample.Where(p => p.Id != 0).Sum(p => p.CpuPercent);
        totalMemory = sample.Sum(p => p.WorkingSet);

        EnsureIcons(sample);
        UpdateColumnHeaders();

        if (countChanged)
        {
            list.VirtualListSize = rows.Count;
            Reselect(selectedId);
        }

        RedrawVisible();
        UpdateSummary();
    }

    void RedrawVisible()
    {
        if (rows.Count == 0 || !list.IsHandleCreated) return;

        try
        {
            var rowHeight = Math.Max(1, list.GetItemRect(0).Height);
            var first = Math.Clamp(list.TopItem?.Index ?? 0, 0, rows.Count - 1);
            var span = list.ClientSize.Height / rowHeight + 2;
            var last = Math.Clamp(first + span, first, rows.Count - 1);
            list.RedrawItems(first, last, false);
        }
        catch (ArgumentOutOfRangeException)
        {
            list.Invalidate();
        }
    }

    static int Compare(ProcessEntry left, ProcessEntry right)
    {
        var byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        return byName != 0 ? byName : left.Id.CompareTo(right.Id);
    }

    static bool IsApp(ProcessEntry process)
    {
        if (process.ExecutablePath.Length == 0) return false;
        if (process.ExecutablePath.Contains(@"\Windows\System32\", StringComparison.OrdinalIgnoreCase)) return false;
        if (process.ExecutablePath.Contains(@"\Windows\SysWOW64\", StringComparison.OrdinalIgnoreCase)) return false;
        return process.WorkingSet > 48L * 1024 * 1024;
    }

    int SelectedProcessId()
    {
        if (list.SelectedIndices.Count == 0) return -1;
        var index = list.SelectedIndices[0];
        return index >= 0 && index < rows.Count ? rows[index].Process?.Id ?? -1 : -1;
    }

    void Reselect(int id)
    {
        if (id < 0) return;
        var index = rows.FindIndex(row => row.Process?.Id == id);
        if (index < 0) return;
        list.SelectedIndices.Clear();
        list.SelectedIndices.Add(index);
    }

    void EnsureIcons(IReadOnlyList<ProcessEntry> sample)
    {
        foreach (var process in sample)
        {
            if (process.ExecutablePath.Length == 0) continue;
            if (iconIndex.ContainsKey(process.ExecutablePath)) continue;

            var image = Icons.ForExecutable(process.ExecutablePath);
            if (image is null)
            {
                iconIndex[process.ExecutablePath] = -1;
                continue;
            }

            icons.Images.Add(image);
            iconIndex[process.ExecutablePath] = icons.Images.Count - 1;
        }
    }

    void UpdateSummary()
    {
        if (!monitor.Available) return;
        var count = rows.Count(row => row.Process is not null);
        summary.Text = Strings.Format(UiText.ActiveSummaryFormat, Humanize.Number(count), Humanize.Bytes(totalMemory));
    }

    void ResizeColumns()
    {
        if (list.Columns.Count < 6 || list.ClientSize.Width <= 0) return;
        var used = list.Columns[0].Width + list.Columns[1].Width + list.Columns[2].Width + list.Columns[3].Width + list.Columns[4].Width;
        list.Columns[5].Width = Math.Max(LogicalToDeviceUnits(80), list.ClientSize.Width - used);
    }

    ListViewItem MakeItem(Row row)
    {
        if (row.Header is not null)
        {
            var header = new ListViewItem(Strings.Get(row.Header.Value));
            for (var column = 1; column < 6; column++) header.SubItems.Add("");
            return header;
        }

        var process = row.Process!;
        var windows = IsWindowsProvided(process.ExecutablePath);
        var item = new ListViewItem(process.Name);
        item.ImageIndex = iconIndex.TryGetValue(process.ExecutablePath, out var index) ? index : -1;
        item.SubItems.Add(windows ? Strings.Get(UiText.OriginWindows) : "");
        item.SubItems[1].ForeColor = Theme.Current.Accent;
        item.SubItems.Add(process.Id.ToString(CultureInfo.CurrentCulture));
        item.SubItems.Add(string.Create(CultureInfo.CurrentCulture, $"{process.CpuPercent:0.0} %"));
        item.SubItems.Add(Humanize.Bytes(process.WorkingSet));
        item.SubItems.Add(process.ExecutablePath);
        item.UseItemStyleForSubItems = false;
        return item;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            menu.Dispose();
            monitor.Dispose();
            icons.Dispose();
        }

        base.Dispose(disposing);
    }
}
