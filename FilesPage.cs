using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DiskLit;

internal sealed class FilesPage : Page
{
    readonly Card headerCard = new();
    readonly TableLayoutPanel headerTop = new();
    readonly Panel headerBottom = new();
    readonly Label driveTitle = new() { AutoSize = true };
    readonly SegmentedBar usageBar = new() { Dock = DockStyle.Top };
    readonly Label usageText = new() { AutoSize = true };
    readonly ThemedButton scanButton = FlatButton();
    readonly ThemedButton stopButton = FlatButton();
    readonly ProgressBar activity = new() { Style = ProgressBarStyle.Marquee, Visible = false, MarqueeAnimationSpeed = 24 };

    readonly Panel sidebar = new();
    readonly Label drivesLabel = SectionLabel();
    readonly ListBox driveList = new() { BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false };
    readonly Label typesLabel = SectionLabel();
    readonly TableLayoutPanel typesTable = new();
    readonly Label foldersLabel = SectionLabel();
    readonly TreeView folderTree = new() { BorderStyle = BorderStyle.None, Dock = DockStyle.Top, HideSelection = false, FullRowSelect = true, ShowLines = false, ShowNodeToolTips = true, ItemHeight = 24, Height = 220 };
    readonly ImageList folderIcons = new() { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(16, 16) };

    readonly Card listCard = new();
    readonly TextBox searchBox = new() { BorderStyle = BorderStyle.FixedSingle };
    readonly TableLayoutPanel filterRow = new();
    readonly ThemedListView list = DetailList(virtualMode: true);
    readonly ImageList icons = new() { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(16, 16) };
    readonly Dictionary<string, int> iconIndex = new(StringComparer.OrdinalIgnoreCase);
    readonly ContextMenuStrip menu = new();
    readonly System.Windows.Forms.Timer filterTimer = new() { Interval = 220 };
    readonly System.Windows.Forms.Timer driveRefreshTimer = new() { Interval = 700 };

    readonly Panel notice = new();
    readonly Label noticeHeadline = new() { AutoSize = true, Dock = DockStyle.Top };
    readonly Label noticeTitle = new() { AutoSize = true, Dock = DockStyle.Top };
    readonly Label noticeBody = new() { AutoSize = false, Dock = DockStyle.Fill };

    readonly Panel statusBar = new();
    readonly Label stats = new() { AutoEllipsis = true, Dock = DockStyle.Fill };
    readonly Label versionLabel = new() { AutoSize = true, Dock = DockStyle.Right };

    CancellationTokenSource? scanCts;
    List<FileEntry> allFiles = [];
    List<FileEntry> visibleFiles = [];
    string? folderFilter;
    ScanResult? lastResult;
    bool refreshingDrives;

    public FilesPage()
    {
        BuildUi();
        LoadDrives();
        WireEvents();
    }

    void BuildUi()
    {
        driveTitle.Font = new Font("Segoe UI Semibold", 11f);
        usageBar.Height = LogicalToDeviceUnits(10);
        usageBar.Margin = new Padding(0);

        headerTop.Dock = DockStyle.Top;
        headerTop.AutoSize = true;
        headerTop.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        headerTop.ColumnCount = 4;
        headerTop.RowCount = 1;
        headerTop.Margin = new Padding(0);
        headerTop.Padding = new Padding(0, 0, 0, 16);
        headerTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        activity.Width = LogicalToDeviceUnits(120);
        activity.Height = LogicalToDeviceUnits(6);
        activity.Margin = new Padding(0, 12, 10, 0);
        headerTop.Controls.Add(driveTitle, 0, 0);
        headerTop.Controls.Add(activity, 1, 0);
        headerTop.Controls.Add(stopButton, 2, 0);
        headerTop.Controls.Add(scanButton, 3, 0);
        driveTitle.Margin = new Padding(0, 10, 0, 0);

        headerBottom.Dock = DockStyle.Top;
        headerBottom.AutoSize = true;
        headerBottom.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        headerBottom.Padding = new Padding(0, 8, 0, 0);
        usageText.Dock = DockStyle.Top;
        headerBottom.Controls.Add(usageText);

        headerCard.Dock = DockStyle.Top;
        headerCard.AutoSize = true;
        headerCard.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        headerCard.Margin = new Padding(0, 0, 0, 12);
        headerCard.Controls.Add(headerBottom);
        headerCard.Controls.Add(usageBar);
        headerCard.Controls.Add(headerTop);

        driveList.ItemHeight = LogicalToDeviceUnits(46);
        driveList.Height = LogicalToDeviceUnits(140);
        driveList.Dock = DockStyle.Top;
        driveList.DrawItem += DrawDriveItem;

        typesTable.Dock = DockStyle.Top;
        typesTable.AutoSize = true;
        typesTable.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        typesTable.ColumnCount = 4;
        typesTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        typesTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        typesTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        typesTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        drivesLabel.Dock = DockStyle.Top;
        typesLabel.Dock = DockStyle.Top;
        typesLabel.Margin = new Padding(0, 16, 0, 6);
        foldersLabel.Dock = DockStyle.Top;
        foldersLabel.Margin = new Padding(0, 16, 0, 6);
        folderTree.ImageList = folderIcons;
        folderTree.HandleCreated += (_, _) => Theme.ApplyNativeTreeStyle(folderTree);
        var folderIcon = Icons.ForFolder();
        if (folderIcon is not null) folderIcons.Images.Add(folderIcon);

        sidebar.Dock = DockStyle.Left;
        sidebar.Width = LogicalToDeviceUnits(264);
        sidebar.Padding = new Padding(0, 0, 18, 0);
        sidebar.Controls.Add(typesTable);
        sidebar.Controls.Add(typesLabel);
        sidebar.Controls.Add(folderTree);
        sidebar.Controls.Add(foldersLabel);
        sidebar.Controls.Add(driveList);
        sidebar.Controls.Add(drivesLabel);

        searchBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        searchBox.Margin = new Padding(0, 0, 0, 10);

        filterRow.Dock = DockStyle.Top;
        filterRow.AutoSize = true;
        filterRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        filterRow.ColumnCount = 1;
        filterRow.RowCount = 1;
        filterRow.Margin = new Padding(0);
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filterRow.Controls.Add(searchBox, 0, 0);

        list.SmallImageList = icons;
        list.Columns.Add("", LogicalToDeviceUnits(250));
        list.Columns.Add("", LogicalToDeviceUnits(110), HorizontalAlignment.Right);
        list.Columns.Add("", LogicalToDeviceUnits(420));
        list.Columns.Add("", LogicalToDeviceUnits(140));
        list.RetrieveVirtualItem += (_, e) => e.Item = MakeItem(visibleFiles[e.ItemIndex]);

        menu.Items.Add(new ToolStripMenuItem("", null, (_, _) => OpenSelectedLocation()));
        menu.Items.Add(new ToolStripMenuItem("", null, (_, _) => CopySelectedPath()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("", null, (_, _) => RecycleSelected()));
        list.ContextMenuStrip = menu;

        noticeHeadline.Font = new Font("Segoe UI Semibold", 10f);
        noticeTitle.Font = new Font("Segoe UI", 8.5f);
        noticeTitle.Margin = new Padding(0, 1, 0, 0);
        noticeBody.Padding = new Padding(0, 5, 0, 0);
        notice.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        notice.Height = LogicalToDeviceUnits(110);
        notice.Padding = new Padding(14, 10, 14, 10);
        notice.Visible = false;
        notice.Controls.Add(noticeBody);
        notice.Controls.Add(noticeTitle);
        notice.Controls.Add(noticeHeadline);
        notice.Paint += PaintNotice;

        list.HideHorizontalScrollBar = true;

        listCard.Dock = DockStyle.Fill;
        listCard.Padding = new Padding(14, 12, 14, 12);
        listCard.Controls.Add(list);
        listCard.Controls.Add(filterRow);
        listCard.Controls.Add(notice);
        listCard.Resize += (_, _) => PositionNotice();

        versionLabel.Font = new Font("Segoe UI", 7.5f);
        versionLabel.Margin = new Padding(0);
        versionLabel.TextAlign = ContentAlignment.MiddleRight;
        versionLabel.Text = "v" + Humanize.Version;
        statusBar.Dock = DockStyle.Bottom;
        statusBar.Height = LogicalToDeviceUnits(26);
        statusBar.Padding = new Padding(2, 6, 2, 0);
        statusBar.Controls.Add(stats);
        statusBar.Controls.Add(versionLabel);

        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(listCard);
        body.Controls.Add(sidebar);

        Controls.Add(body);
        Controls.Add(statusBar);
        Controls.Add(headerCard);
    }

    void PaintNotice(object? sender, PaintEventArgs e)
    {
        var palette = Theme.Current;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var path = Draw.RoundedRectangle(new Rectangle(0, 0, notice.Width - 1, notice.Height - 1), 8);
        using var fill = new SolidBrush(palette.Notice);
        using var edge = new Pen(palette.NoticeBorder, 1.6f);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(edge, path);
    }

    void LoadDrives()
    {
        RefreshDrives();
    }

    public void NotifyStorageChanged()
    {
        driveRefreshTimer.Stop();
        driveRefreshTimer.Start();
    }

    void RefreshDrives()
    {
        var selectedRoot = (driveList.SelectedItem as DriveChoice)?.Root;
        var choices = new List<DriveChoice>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady) choices.Add(new DriveChoice(drive));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }

        refreshingDrives = true;
        driveList.BeginUpdate();
        driveList.Items.Clear();
        foreach (var choice in choices) driveList.Items.Add(choice);
        var selectedIndex = selectedRoot is null
            ? (choices.Count > 0 ? 0 : -1)
            : choices.FindIndex(choice => string.Equals(choice.Root, selectedRoot, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0 && choices.Count > 0) selectedIndex = 0;
        driveList.SelectedIndex = selectedIndex;
        driveList.EndUpdate();
        refreshingDrives = false;

        if (selectedRoot is not null && !choices.Any(choice => string.Equals(choice.Root, selectedRoot, StringComparison.OrdinalIgnoreCase)))
        {
            scanCts?.Cancel();
            ClearResults();
        }
        else
        {
            UpdateHeader();
        }
    }

    void WireEvents()
    {
        scanButton.Click += async (_, _) => await StartScanAsync();
        stopButton.Click += (_, _) => scanCts?.Cancel();
        driveList.SelectedIndexChanged += (_, _) => { if (!refreshingDrives) ClearResults(); };
        driveRefreshTimer.Tick += (_, _) => { driveRefreshTimer.Stop(); RefreshDrives(); };
        searchBox.TextChanged += (_, _) => { filterTimer.Stop(); filterTimer.Start(); };
        filterTimer.Tick += (_, _) => { filterTimer.Stop(); ApplyFilter(); };
        folderTree.AfterSelect += (_, e) =>
        {
            folderFilter = (e.Node?.Tag as FolderSummary)?.FullPath;
            ApplyFilter();
        };
        list.DoubleClick += (_, _) => OpenSelectedLocation();
        list.Resize += (_, _) => ResizeColumns();
        list.SelectedIndexChanged += (_, _) => ShowNotice();
        Resize += (_, _) => ShowNotice();
    }

    public override void ApplyLanguage()
    {
        scanButton.Text = Strings.Get(UiText.Analyze);
        stopButton.Text = Strings.Get(UiText.Stop);
        searchBox.PlaceholderText = Strings.Get(UiText.FilterPlaceholder);
        drivesLabel.Text = Strings.Get(UiText.SectionDrives);
        typesLabel.Text = Strings.Get(UiText.SectionFileTypes);
        foldersLabel.Text = Strings.Get(UiText.SectionFolders);
        list.Columns[0].Text = Strings.Get(UiText.ColumnName);
        list.Columns[1].Text = Strings.Get(UiText.ColumnSize);
        list.Columns[2].Text = Strings.Get(UiText.ColumnLocation);
        list.Columns[3].Text = Strings.Get(UiText.ColumnModified);
        menu.Items[0].Text = Strings.Get(UiText.OpenLocation);
        menu.Items[1].Text = Strings.Get(UiText.CopyPath);
        menu.Items[3].Text = Strings.Get(UiText.MoveToRecycleBin);
        noticeHeadline.Text = Strings.Get(UiText.DoNotDeleteHeadline);
        noticeTitle.Text = Strings.Get(UiText.SystemFileTitle);
        if (lastResult is null) stats.Text = Strings.Get(UiText.Ready);
        UpdateHeader();
        RefreshFolderChoices(lastResult?.Folders);
        RefreshTypes();
        ShowNotice();
    }

    public override void ApplyTheme(Palette palette)
    {
        BackColor = palette.Background;

        headerCard.Fill = palette.Surface;
        headerCard.Edge = palette.Border;
        listCard.Fill = palette.Surface;
        listCard.Edge = palette.Border;

        headerTop.BackColor = palette.Surface;
        headerBottom.BackColor = palette.Surface;
        driveTitle.BackColor = palette.Surface;
        driveTitle.ForeColor = palette.Text;
        usageText.BackColor = palette.Surface;
        usageText.ForeColor = palette.MutedText;
        usageBar.BackColor = palette.Surface;
        usageBar.Track = palette.Subtle;
        activity.BackColor = palette.Surface;

        sidebar.BackColor = palette.Background;
        drivesLabel.BackColor = palette.Background;
        drivesLabel.ForeColor = palette.MutedText;
        typesLabel.BackColor = palette.Background;
        typesLabel.ForeColor = palette.MutedText;
        typesTable.BackColor = palette.Background;
        driveList.BackColor = palette.Background;
        driveList.ForeColor = palette.Text;

        filterRow.BackColor = palette.Surface;
        searchBox.BackColor = palette.Surface;
        searchBox.ForeColor = palette.Text;
        folderTree.BackColor = palette.Background;
        folderTree.ForeColor = palette.Text;
        foldersLabel.BackColor = palette.Background;
        foldersLabel.ForeColor = palette.MutedText;
        list.BackColor = palette.Surface;
        list.ForeColor = palette.Text;

        statusBar.BackColor = palette.Background;
        stats.BackColor = palette.Background;
        stats.ForeColor = palette.MutedText;
        versionLabel.BackColor = palette.Background;
        versionLabel.ForeColor = Color.FromArgb(120, palette.MutedText);

        noticeHeadline.BackColor = palette.Notice;
        noticeHeadline.ForeColor = palette.Danger;
        noticeTitle.BackColor = palette.Notice;
        noticeTitle.ForeColor = palette.MutedText;
        noticeBody.BackColor = palette.Notice;
        noticeBody.ForeColor = palette.Text;
        notice.BackColor = palette.Notice;

        Skin(scanButton, palette.Accent, palette.AccentText, palette);
        Skin(stopButton, palette.Subtle, palette.SubtleText, palette);

        menu.BackColor = palette.Surface;
        menu.ForeColor = palette.Text;
        menu.Renderer = new ThemedMenuRenderer(palette);

        if (lastResult is null) stats.Text = Strings.Get(UiText.Intro);

        RefreshTypes();
        if (list.IsHandleCreated) Theme.ApplyNativeListStyle(list);
        if (folderTree.IsHandleCreated) Theme.ApplyNativeTreeStyle(folderTree);
        driveList.Invalidate();
        folderTree.Invalidate();
        notice.Invalidate();
    }

    void DrawDriveItem(object? sender, DrawItemEventArgs e)
    {
        var palette = Theme.Current;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var selected = (e.State & DrawItemState.Selected) != 0;
        var area = Rectangle.Inflate(e.Bounds, -1, -2);

        using (var background = new SolidBrush(selected ? palette.Surface : palette.Background))
        using (var path = Draw.RoundedRectangle(area, 8))
        {
            e.Graphics.FillPath(background, path);
            if (selected)
            {
                using var edge = new Pen(palette.Border);
                e.Graphics.DrawPath(edge, path);
            }
        }

        if (e.Index < 0 || e.Index >= driveList.Items.Count) return;
        if (driveList.Items[e.Index] is not DriveChoice choice) return;

        using var title = new SolidBrush(palette.Text);
        using var detail = new SolidBrush(palette.MutedText);
        using var titleFont = new Font("Segoe UI Semibold", 9.5f);
        e.Graphics.DrawString(choice.Label, titleFont, title, area.X + 10, area.Y + 6);
        e.Graphics.DrawString(choice.Usage, Font, detail, area.X + 10, area.Y + 24);
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
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        e.Graphics.DrawString(combo.Items[e.Index]?.ToString(), e.Font ?? combo.Font, foreground, e.Bounds, format);
    }

    void ClearResults()
    {
        allFiles = [];
        visibleFiles = [];
        folderFilter = null;
        lastResult = null;
        searchBox.Clear();
        list.VirtualListSize = 0;
        notice.Visible = false;
        icons.Images.Clear();
        iconIndex.Clear();
        RefreshFolderChoices();
        RefreshTypes();
        UpdateHeader();
        stats.Text = Strings.Get(UiText.Intro);
        list.Invalidate();
    }

    void UpdateHeader()
    {
        if (driveList.SelectedItem is not DriveChoice choice)
        {
            driveTitle.Text = "";
            usageText.Text = "";
            return;
        }

        driveTitle.Text = choice.Label;
        var used = choice.TotalSize - choice.AvailableFreeSpace;
        usageText.Text = Strings.Format(UiText.UsageFormat,
            Humanize.Bytes(used), Humanize.Bytes(choice.TotalSize), Humanize.Bytes(choice.AvailableFreeSpace));

        if (lastResult is null || !string.Equals(lastResult.Root, choice.Root, StringComparison.OrdinalIgnoreCase))
        {
            usageBar.SetSegments([(Theme.Current.MutedText, used)], choice.TotalSize);
            return;
        }

        usageBar.SetSegments(Segments(lastResult), choice.TotalSize);
    }

    static List<(Color, long)> Segments(ScanResult result)
    {
        var segments = new List<(Color, long)>();
        foreach (var category in FileTypes.All)
        {
            var value = result.CategoryBytes[(int)category];
            if (value > 0) segments.Add((FileTypes.Swatch(category), value));
        }

        return segments;
    }

    void RefreshTypes()
    {
        typesTable.SuspendLayout();
        typesTable.Controls.Clear();
        typesTable.RowStyles.Clear();

        var palette = Theme.Current;
        var total = lastResult is null ? 0 : lastResult.Bytes;
        var ordered = FileTypes.All
            .Select(category => (Category: category, Bytes: lastResult is null ? 0 : lastResult.CategoryBytes[(int)category]))
            .OrderByDescending(entry => entry.Bytes)
            .ToList();

        var row = 0;
        foreach (var (category, bytes) in ordered)
        {
            if (lastResult is not null && bytes == 0) continue;

            var swatch = new Swatch
            {
                Color = FileTypes.Swatch(category),
                Size = new Size(LogicalToDeviceUnits(9), LogicalToDeviceUnits(9)),
                Margin = new Padding(0, 6, 8, 6),
                BackColor = palette.Background
            };
            var legendFont = new Font("Segoe UI", 8.5f);
            var name = new Label
            {
                AutoSize = true,
                AutoEllipsis = true,
                Font = legendFont,
                Text = Strings.Get(FileTypes.Label(category)),
                ForeColor = palette.Text,
                BackColor = palette.Background,
                Margin = new Padding(0, 3, 8, 3)
            };
            var size = new Label
            {
                AutoSize = true,
                Font = legendFont,
                Text = lastResult is null ? "—" : Humanize.Bytes(bytes),
                ForeColor = palette.MutedText,
                BackColor = palette.Background,
                Margin = new Padding(0, 3, 8, 3)
            };
            var share = new Label
            {
                AutoSize = true,
                Font = legendFont,
                Text = total > 0 ? string.Create(CultureInfo.CurrentCulture, $"{bytes * 100.0 / total:0}%") : "",
                ForeColor = palette.MutedText,
                BackColor = palette.Background,
                Margin = new Padding(0, 3, 0, 3)
            };

            typesTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            typesTable.Controls.Add(swatch, 0, row);
            typesTable.Controls.Add(name, 1, row);
            typesTable.Controls.Add(size, 2, row);
            typesTable.Controls.Add(share, 3, row);
            row++;
        }

        typesTable.RowCount = Math.Max(1, row);
        typesTable.ResumeLayout();
    }

    void ResizeColumns()
    {
        if (list.Columns.Count < 4 || list.ClientSize.Width <= 0) return;

        var name = LogicalToDeviceUnits(230);
        var size = LogicalToDeviceUnits(100);
        var modified = LogicalToDeviceUnits(130);
        var available = list.ClientSize.Width;
        var location = available - name - size - modified;

        if (location < LogicalToDeviceUnits(120))
        {
            var deficit = LogicalToDeviceUnits(120) - location;
            name = Math.Max(LogicalToDeviceUnits(110), name - deficit);
            location = available - name - size - modified;
        }

        list.Columns[0].Width = name;
        list.Columns[1].Width = size;
        list.Columns[2].Width = Math.Max(LogicalToDeviceUnits(60), location);
        list.Columns[3].Width = modified;
    }

    async Task StartScanAsync()
    {
        if (driveList.SelectedItem is not DriveChoice selected) return;

        scanCts?.Dispose();
        var currentCts = new CancellationTokenSource();
        scanCts = currentCts;
        var token = currentCts.Token;

        allFiles = [];
        visibleFiles = [];
        folderFilter = null;
        lastResult = null;
        list.VirtualListSize = 0;
        notice.Visible = false;
        RefreshFolderChoices();
        RefreshTypes();
        SetScanning(true);
        Busy = true;

        var progress = new Progress<ScanProgress>(p =>
        {
            if (IsDisposed || Disposing || token.IsCancellationRequested) return;
            stats.Text = Strings.Format(UiText.ProgressFormat,
                Humanize.Number(p.Files), Humanize.Bytes(p.Bytes), Humanize.Number(p.Inaccessible), p.Current);
            if (p.Largest is null) return;
            allFiles = p.Largest;
            ApplyFilter();
        });

        try
        {
            var root = selected.Root;
            var result = await Task.Run(() => NativeScanner.Scan(root, token, progress), token);
            lastResult = result;
            allFiles = result.Largest;
            EnsureIcons(allFiles);
            RefreshFolderChoices(result.Folders);
            RefreshTypes();
            UpdateHeader();
            ApplyFilter();

            var speed = result.Elapsed.TotalSeconds > 0
                ? Strings.Format(UiText.SpeedFormat, Humanize.Number((long)(result.Files / result.Elapsed.TotalSeconds)))
                : "";
            stats.Text = Strings.Format(UiText.StatsFormat,
                Humanize.Number(result.Files), Humanize.Number(result.Directories),
                result.Elapsed.ToString(@"mm\:ss", CultureInfo.CurrentCulture)) + "  ·  " + speed;
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && !Disposing) stats.Text = Strings.Get(UiText.ScanStopped);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            if (!IsDisposed && !Disposing) stats.Text = Strings.Get(UiText.ScanFailedPrefix) + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(scanCts, currentCts)) scanCts = null;
            currentCts.Dispose();
            if (!IsDisposed && !Disposing)
            {
                SetScanning(false);
                Busy = false;
            }
        }
    }

    public override void CancelWork()
    {
        filterTimer.Stop();
        driveRefreshTimer.Stop();
        scanCts?.Cancel();
    }

    void EnsureIcons(List<FileEntry> files)
    {
        foreach (var file in files)
        {
            var key = IconKey(file.Name);
            if (iconIndex.ContainsKey(key)) continue;

            var image = Icons.ForFileName(file.Name);
            if (image is null)
            {
                iconIndex[key] = -1;
                continue;
            }

            icons.Images.Add(image);
            iconIndex[key] = icons.Images.Count - 1;
        }
    }

    static string IconKey(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[dot..].ToLowerInvariant();
    }

    void RefreshFolderChoices(IReadOnlyList<FolderSummary>? folders = null)
    {
        folderTree.BeginUpdate();
        folderTree.Nodes.Clear();
        var root = new TreeNode(Strings.Get(UiText.AllFolders)) { ImageIndex = 0, SelectedImageIndex = 0 };
        folderTree.Nodes.Add(root);
        if (folders is not null)
        {
            var nodes = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders.OrderBy(entry => entry.Depth).ThenByDescending(entry => entry.TotalBytes))
            {
                var path = folder.FullPath.TrimEnd(Path.DirectorySeparatorChar);
                var name = Path.GetFileName(path);
                if (name.Length == 0) name = folder.Display;
                var node = new TreeNode($"{name}  ·  {Humanize.Bytes(folder.TotalBytes)}  ·  {Humanize.Number(folder.TotalFiles)}")
                {
                    Tag = folder, ToolTipText = folder.FullPath, ImageIndex = 0, SelectedImageIndex = 0
                };
                var parentPath = Path.GetDirectoryName(path);
                if (parentPath is not null && nodes.TryGetValue(parentPath, out var parent)) parent.Nodes.Add(node);
                else root.Nodes.Add(node);
                nodes[path] = node;
            }
        }
        root.Expand();
        folderTree.SelectedNode = root;
        folderTree.EndUpdate();
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
        ShowNotice();
    }

    static bool IsUnder(string path, string folder) =>
        path.Length > folder.Length
        && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
        && (folder.EndsWith(Path.DirectorySeparatorChar) || path[folder.Length] == Path.DirectorySeparatorChar);

    void SetScanning(bool scanning)
    {
        scanButton.Enabled = !scanning;
        driveList.Enabled = !scanning;
        stopButton.Enabled = scanning;
        activity.Visible = scanning;
    }

    void ShowNotice()
    {
        var file = Selected();
        var explanation = file is null ? null : SystemFiles.Describe(file.FullPath);
        if (explanation is null)
        {
            notice.Visible = false;
            return;
        }

        var text = Strings.Get(explanation.Value);
        noticeBody.Text = text;

        var available = Math.Max(LogicalToDeviceUnits(200), listCard.ClientSize.Width - listCard.Padding.Horizontal - notice.Padding.Horizontal);
        var measured = TextRenderer.MeasureText(text, noticeBody.Font,
            new Size(available, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        notice.Height = measured.Height + noticeHeadline.Height + noticeTitle.Height
            + notice.Padding.Vertical + LogicalToDeviceUnits(10);
        notice.Visible = true;
        PositionNotice();
        notice.BringToFront();
    }

    void PositionNotice()
    {
        if (!notice.Visible) return;
        var inner = listCard.ClientSize;
        notice.Width = Math.Max(LogicalToDeviceUnits(220), inner.Width - listCard.Padding.Horizontal);
        notice.Left = listCard.Padding.Left;
        notice.Top = Math.Max(0, inner.Height - listCard.Padding.Bottom - notice.Height);
    }

    ListViewItem MakeItem(FileEntry file)
    {
        var system = SystemFiles.IsSystemItem(file.FullPath);
        var item = new ListViewItem(file.Name);
        item.ImageIndex = iconIndex.TryGetValue(IconKey(file.Name), out var index) ? index : -1;
        item.SubItems.Add(Humanize.Bytes(file.Size));
        item.SubItems.Add(Path.GetDirectoryName(file.FullPath) ?? "");
        item.SubItems.Add(file.Modified == DateTime.MinValue
            ? Strings.Get(UiText.UnknownDate)
            : file.Modified.ToString("g", CultureInfo.CurrentCulture));
        if (system) item.ForeColor = Theme.Current.MutedText;
        item.Tag = file;
        return item;
    }

    FileEntry? Selected() =>
        list.SelectedIndices.Count == 0 || list.SelectedIndices[0] >= visibleFiles.Count
            ? null
            : visibleFiles[list.SelectedIndices[0]];

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

    void RecycleSelected()
    {
        var file = Selected();
        if (file is null) return;

        var message = Strings.Format(UiText.RecycleConfirmFormat, file.Name, Humanize.Bytes(file.Size));
        if (SystemFiles.IsSystemItem(file.FullPath))
            message = Strings.Get(UiText.RecycleSystemWarning) + "\n\n" + message;

        var answer = MessageBox.Show(message, Strings.Get(UiText.RecycleConfirmTitle),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        if (!Cleanup.RecycleChosen(file.FullPath))
        {
            MessageBox.Show(Strings.Get(UiText.RecycleFailed), Strings.Get(UiText.RecycleConfirmTitle),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        allFiles = allFiles.Where(entry => !string.Equals(entry.FullPath, file.FullPath, StringComparison.OrdinalIgnoreCase)).ToList();
        ApplyFilter();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelWork();
            scanCts = null;
            filterTimer.Dispose();
            driveRefreshTimer.Dispose();
            menu.Dispose();
            icons.Dispose();
            folderIcons.Dispose();
        }

        base.Dispose(disposing);
    }

    sealed class DriveChoice
    {
        public DriveChoice(DriveInfo drive)
        {
            Root = drive.RootDirectory.FullName;
            TotalSize = drive.TotalSize;
            AvailableFreeSpace = drive.AvailableFreeSpace;
            var name = drive.Name.TrimEnd(Path.DirectorySeparatorChar);
            var format = drive.DriveFormat;
            Label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? $"{name}  —  {format}"
                : $"{name}  {drive.VolumeLabel} — {format}";
        }

        public string Root { get; }
        public long TotalSize { get; }
        public long AvailableFreeSpace { get; }
        public string Label { get; }

        public string Usage =>
            $"{Humanize.Bytes(TotalSize - AvailableFreeSpace)} de {Humanize.Bytes(TotalSize)}";

        public override string ToString() => Label;
    }

}
