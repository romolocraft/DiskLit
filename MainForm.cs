using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DiskLit;

internal sealed class MainForm : Form
{
    const int WmSettingChange = 0x001A;

    readonly ComboBox driveBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
    readonly ComboBox folderBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Enabled = false };
    readonly Button scanButton = new();
    readonly Button stopButton = new() { Enabled = false };
    readonly Button themeButton = new();
    readonly TextBox searchBox = new() { BorderStyle = BorderStyle.FixedSingle };
    readonly Label headline = new() { AutoSize = true };
    readonly Label summary = new() { AutoSize = true };
    readonly Label status = new() { AutoEllipsis = true };
    readonly ProgressBar activity = new() { Style = ProgressBarStyle.Marquee, Visible = false, MarqueeAnimationSpeed = 24 };
    readonly ListView list = new() { View = View.Details, FullRowSelect = true, GridLines = false, VirtualMode = true };
    readonly ContextMenuStrip menu = new();
    readonly System.Windows.Forms.Timer filterTimer = new() { Interval = 220 };
    readonly TableLayoutPanel header = new();
    readonly Panel footer = new();
    readonly TableLayoutPanel controlsRow = new();
    CancellationTokenSource? scanCts;
    List<FileEntry> allFiles = [];
    List<FileEntry> visibleFiles = [];
    string? folderFilter;

    public MainForm()
    {
        Strings.UseSystemLanguage();
        Theme.Use(ThemeMode.System);

        AutoScaleMode = AutoScaleMode.Font;
        MinimumSize = new Size(880, 580);
        Size = new Size(1180, 740);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        DoubleBuffered = true;

        BuildUi();
        LoadDrives();
        WireEvents();
        ApplyLanguage();
        ApplyTheme();
    }

    void BuildUi()
    {
        headline.Font = new Font("Segoe UI Semibold", 19);
        headline.Margin = new Padding(0, 0, 0, 2);
        summary.Margin = new Padding(2, 0, 0, 14);

        driveBox.Width = LogicalToDeviceUnits(270);
        folderBox.Width = LogicalToDeviceUnits(240);
        searchBox.MinimumSize = new Size(LogicalToDeviceUnits(150), 0);
        searchBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        foreach (var box in new[] { driveBox, folderBox })
        {
            box.DrawMode = DrawMode.OwnerDrawFixed;
            box.ItemHeight = LogicalToDeviceUnits(20);
            box.DrawItem += DrawComboItem;
        }

        foreach (var button in new[] { scanButton, stopButton, themeButton })
        {
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(14, 5, 14, 5);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Cursor = Cursors.Hand;
        }

        controlsRow.Dock = DockStyle.Fill;
        controlsRow.AutoSize = true;
        controlsRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        controlsRow.ColumnCount = 6;
        controlsRow.RowCount = 1;
        controlsRow.Margin = new Padding(0);
        controlsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controlsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controlsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controlsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controlsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controlsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controlsRow.Controls.Add(driveBox, 0, 0);
        controlsRow.Controls.Add(scanButton, 1, 0);
        controlsRow.Controls.Add(stopButton, 2, 0);
        controlsRow.Controls.Add(folderBox, 3, 0);
        controlsRow.Controls.Add(searchBox, 4, 0);
        controlsRow.Controls.Add(themeButton, 5, 0);

        header.Dock = DockStyle.Top;
        header.AutoSize = true;
        header.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        header.ColumnCount = 1;
        header.RowCount = 3;
        header.Padding = new Padding(22, 18, 22, 14);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.Controls.Add(headline);
        header.Controls.Add(summary);
        header.Controls.Add(controlsRow);

        footer.Dock = DockStyle.Bottom;
        footer.AutoSize = true;
        footer.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        footer.Padding = new Padding(18, 12, 18, 10);
        status.Dock = DockStyle.Fill;
        activity.Dock = DockStyle.Right;
        activity.Width = LogicalToDeviceUnits(180);
        footer.Controls.Add(status);
        footer.Controls.Add(activity);

        list.Dock = DockStyle.Fill;
        list.BorderStyle = BorderStyle.None;
        list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        list.Columns.Add("", LogicalToDeviceUnits(260));
        list.Columns.Add("", LogicalToDeviceUnits(120), HorizontalAlignment.Right);
        list.Columns.Add("", LogicalToDeviceUnits(520));
        list.Columns.Add("", LogicalToDeviceUnits(150));
        list.RetrieveVirtualItem += (_, e) => e.Item = MakeItem(visibleFiles[e.ItemIndex]);

        menu.Items.Add(new ToolStripMenuItem("", null, (_, _) => OpenSelectedLocation()));
        menu.Items.Add(new ToolStripMenuItem("", null, (_, _) => CopySelectedPath()));
        list.ContextMenuStrip = menu;

        Controls.Add(list);
        Controls.Add(footer);
        Controls.Add(header);
    }

    void LoadDrives()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            driveBox.Items.Add(new DriveChoice(drive));
        if (driveBox.Items.Count > 0) driveBox.SelectedIndex = 0;
    }

    void WireEvents()
    {
        scanButton.Click += async (_, _) => await StartScanAsync();
        stopButton.Click += (_, _) => scanCts?.Cancel();
        themeButton.Click += (_, _) => CycleTheme();
        searchBox.TextChanged += (_, _) => { filterTimer.Stop(); filterTimer.Start(); };
        filterTimer.Tick += (_, _) => { filterTimer.Stop(); ApplyFilter(); };
        folderBox.SelectedIndexChanged += (_, _) =>
        {
            folderFilter = (folderBox.SelectedItem as FolderChoice)?.Summary?.FullPath;
            ApplyFilter();
        };
        list.DoubleClick += (_, _) => OpenSelectedLocation();
        list.Resize += (_, _) => ResizeColumns();
        FormClosing += (_, _) => scanCts?.Cancel();
    }

    void ApplyLanguage()
    {
        Text = Strings.Get(UiText.AppTitle);
        headline.Text = Strings.Get(UiText.Headline);
        summary.Text = Strings.Get(UiText.Intro);
        scanButton.Text = Strings.Get(UiText.Analyze);
        stopButton.Text = Strings.Get(UiText.Stop);
        searchBox.PlaceholderText = Strings.Get(UiText.FilterPlaceholder);
        status.Text = Strings.Get(UiText.Ready);
        list.Columns[0].Text = Strings.Get(UiText.ColumnName);
        list.Columns[1].Text = Strings.Get(UiText.ColumnSize);
        list.Columns[2].Text = Strings.Get(UiText.ColumnLocation);
        list.Columns[3].Text = Strings.Get(UiText.ColumnModified);
        menu.Items[0].Text = Strings.Get(UiText.OpenLocation);
        menu.Items[1].Text = Strings.Get(UiText.CopyPath);
        themeButton.Text = Strings.Get(Theme.Mode switch
        {
            ThemeMode.Light => UiText.ThemeLight,
            ThemeMode.Dark => UiText.ThemeDark,
            _ => UiText.ThemeSystem
        });
        RefreshFolderChoices();
    }

    void CycleTheme()
    {
        Theme.Use(Theme.Mode switch
        {
            ThemeMode.System => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.Dark,
            _ => ThemeMode.System
        });
        ApplyLanguage();
        ApplyTheme();
    }

    void ApplyTheme()
    {
        var palette = Theme.Current;
        SuspendLayout();

        BackColor = palette.Background;
        ForeColor = palette.Text;
        header.BackColor = palette.Surface;
        footer.BackColor = palette.Surface;
        controlsRow.BackColor = palette.Surface;
        headline.BackColor = palette.Surface;
        headline.ForeColor = palette.Text;
        summary.BackColor = palette.Surface;
        summary.ForeColor = palette.MutedText;
        status.BackColor = palette.Surface;
        status.ForeColor = palette.MutedText;

        list.BackColor = palette.Surface;
        list.ForeColor = palette.Text;
        searchBox.BackColor = palette.Surface;
        searchBox.ForeColor = palette.Text;

        foreach (var box in new[] { driveBox, folderBox })
        {
            box.BackColor = palette.Surface;
            box.ForeColor = palette.Text;
        }

        PaintButton(scanButton, palette.Accent, palette.AccentText, palette);
        PaintButton(stopButton, palette.Subtle, palette.SubtleText, palette);
        PaintButton(themeButton, palette.Subtle, palette.SubtleText, palette);

        menu.BackColor = palette.Surface;
        menu.ForeColor = palette.Text;
        menu.Renderer = new ThemedMenuRenderer(palette);

        if (IsHandleCreated)
        {
            Theme.ApplyTitleBar(this);
            Theme.ApplyNativeListStyle(list);
        }

        ResumeLayout();
        Invalidate(true);
    }

    static void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        var palette = Theme.Current;
        var highlighted = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using var background = new SolidBrush(highlighted ? palette.Selection : palette.Surface);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (e.Index < 0) return;
        using var foreground = new SolidBrush(combo.Enabled ? palette.Text : palette.MutedText);
        using var format = new StringFormat(StringFormatFlags.NoWrap) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        e.Graphics.DrawString(combo.Items[e.Index]?.ToString(), e.Font ?? combo.Font, foreground, e.Bounds, format);
    }

    static void PaintButton(Button button, Color back, Color fore, Palette palette)
    {
        button.BackColor = back;
        button.ForeColor = fore;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.2f);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(back, 0.05f);
        button.FlatAppearance.BorderColor = palette.Border;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this);
        Theme.ApplyNativeListStyle(list);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WmSettingChange || Theme.Mode != ThemeMode.System) return;
        var section = m.LParam == IntPtr.Zero ? null : Marshal.PtrToStringUni(m.LParam);
        if (section is not "ImmersiveColorSet") return;
        var wasDark = Theme.IsDark;
        Theme.Refresh();
        if (Theme.IsDark != wasDark) ApplyTheme();
    }

    void ResizeColumns()
    {
        if (list.Columns.Count < 4) return;
        var used = list.Columns[0].Width + list.Columns[1].Width + list.Columns[3].Width;
        var remaining = list.ClientSize.Width - used - SystemInformation.VerticalScrollBarWidth;
        list.Columns[2].Width = Math.Max(LogicalToDeviceUnits(160), remaining);
    }

    async Task StartScanAsync()
    {
        if (driveBox.SelectedItem is not DriveChoice selected) return;

        scanCts?.Dispose();
        scanCts = new CancellationTokenSource();
        var token = scanCts.Token;

        allFiles = [];
        visibleFiles = [];
        folderFilter = null;
        list.VirtualListSize = 0;
        RefreshFolderChoices();
        SetScanning(true);

        var progress = new Progress<ScanProgress>(p =>
        {
            status.Text = string.Format(CultureInfo.CurrentCulture, Strings.Get(UiText.ProgressFormat),
                FormatNumber(p.Files), FormatBytes(p.Bytes), FormatNumber(p.Inaccessible), p.Current);
            if (p.Largest is null) return;
            allFiles = p.Largest;
            ApplyFilter();
        });

        try
        {
            var root = selected.Drive.RootDirectory.FullName;
            var result = await Task.Run(() => NativeScanner.Scan(root, token, progress), token);
            allFiles = result.Largest;
            RefreshFolderChoices(result.Folders);
            ApplyFilter();
            summary.Text = string.Format(CultureInfo.CurrentCulture, Strings.Get(UiText.SummaryFormat),
                FormatNumber(result.Files), FormatBytes(result.Bytes),
                result.Elapsed.ToString(@"mm\:ss", CultureInfo.CurrentCulture),
                Strings.Get(result.IsSolidState ? UiText.SolidState : UiText.Sequential),
                FormatNumber(result.InaccessibleEntries));
            status.Text = string.Format(CultureInfo.CurrentCulture, Strings.Get(UiText.DoneFormat),
                FormatNumber(allFiles.Count));
        }
        catch (OperationCanceledException)
        {
            status.Text = Strings.Get(UiText.ScanStopped);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            status.Text = Strings.Get(UiText.ScanFailedPrefix) + ex.Message;
        }
        finally
        {
            SetScanning(false);
        }
    }

    void RefreshFolderChoices(IReadOnlyList<FolderSummary>? folders = null)
    {
        folderBox.BeginUpdate();
        folderBox.Items.Clear();
        folderBox.Items.Add(new FolderChoice(null));
        if (folders is not null)
            foreach (var folder in folders)
                folderBox.Items.Add(new FolderChoice(folder));
        folderBox.SelectedIndex = 0;
        folderBox.Enabled = folders is { Count: > 0 };
        folderBox.EndUpdate();
    }

    void ApplyFilter()
    {
        var term = searchBox.Text.Trim();
        var folder = folderFilter;

        if (term.Length == 0 && folder is null)
        {
            visibleFiles = allFiles;
        }
        else
        {
            var filtered = new List<FileEntry>(Math.Min(allFiles.Count, 1024));
            foreach (var file in allFiles)
            {
                if (folder is not null && !IsUnder(file.FullPath, folder)) continue;
                if (term.Length > 0 && !file.FullPath.Contains(term, StringComparison.OrdinalIgnoreCase)) continue;
                filtered.Add(file);
            }
            visibleFiles = filtered;
        }

        list.VirtualListSize = visibleFiles.Count;
        list.Invalidate();
    }

    static bool IsUnder(string path, string folder) =>
        path.Length > folder.Length
        && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
        && (folder.EndsWith(Path.DirectorySeparatorChar) || path[folder.Length] == Path.DirectorySeparatorChar);

    void SetScanning(bool scanning)
    {
        scanButton.Enabled = !scanning;
        driveBox.Enabled = !scanning;
        stopButton.Enabled = scanning;
        activity.Visible = scanning;
        if (scanning) summary.Text = Strings.Get(UiText.ScanningHint);
    }

    static ListViewItem MakeItem(FileEntry file)
    {
        var item = new ListViewItem(file.Name);
        item.SubItems.Add(FormatBytes(file.Size));
        item.SubItems.Add(Path.GetDirectoryName(file.FullPath) ?? "");
        item.SubItems.Add(file.Modified == DateTime.MinValue
            ? Strings.Get(UiText.UnknownDate)
            : file.Modified.ToString("g", CultureInfo.CurrentCulture));
        item.Tag = file;
        return item;
    }

    FileEntry? Selected() => list.SelectedIndices.Count == 0 ? null : visibleFiles[list.SelectedIndices[0]];

    void OpenSelectedLocation()
    {
        var file = Selected();
        if (file is null) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file.FullPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(ex.Message, Strings.Get(UiText.CouldNotOpenTitle), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void CopySelectedPath()
    {
        var file = Selected();
        if (file is null) return;
        try
        {
            Clipboard.SetText(file.FullPath);
        }
        catch (ExternalException ex)
        {
            MessageBox.Show(ex.Message, Strings.Get(UiText.CouldNotCopyTitle), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    static string FormatNumber(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return string.Create(CultureInfo.CurrentCulture, $"{size:0.##} {units[unit]}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            scanCts?.Cancel();
            scanCts?.Dispose();
            scanCts = null;
            filterTimer.Dispose();
            menu.Dispose();
        }
        base.Dispose(disposing);
    }

    sealed class DriveChoice(DriveInfo drive)
    {
        public DriveInfo Drive { get; } = drive;

        public override string ToString() => string.Format(CultureInfo.CurrentCulture, Strings.Get(UiText.DriveFormat),
            Drive.Name, Drive.VolumeLabel, FormatBytes(Drive.TotalSize - Drive.AvailableFreeSpace), FormatBytes(Drive.TotalSize));
    }

    sealed class FolderChoice(FolderSummary? summary)
    {
        public FolderSummary? Summary { get; } = summary;

        public override string ToString() => Summary is null
            ? Strings.Get(UiText.AllFolders)
            : $"{new string(' ', (Summary.Depth - 1) * 3)}{Summary.Display}  ·  {FormatBytes(Summary.TotalBytes)}";
    }
}
