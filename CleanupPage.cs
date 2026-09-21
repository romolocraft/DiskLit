namespace DiskLit;

internal sealed class CleanupPage : Page
{
    const int MaxItemsPerGroup = 4_000;

    readonly ThemedButton scanButton = FlatButton();
    readonly ThemedButton runButton = FlatButton();
    readonly Card card = new();
    readonly TreeView tree = new()
    {
        CheckBoxes = true,
        BorderStyle = BorderStyle.None,
        Dock = DockStyle.Fill,
        ShowLines = false,
        ShowRootLines = false,
        ShowPlusMinus = true,
        FullRowSelect = true,
        HideSelection = false,
        ItemHeight = 24
    };
    readonly ImageList icons = new() { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(16, 16) };
    readonly Dictionary<string, int> iconIndex = new(StringComparer.OrdinalIgnoreCase);
    readonly Label hint = new() { AutoSize = false, Dock = DockStyle.Top };
    readonly FlowLayoutPanel buttonRow = new();
    readonly Panel statusBar = new();
    readonly Label status = new() { AutoEllipsis = true, Dock = DockStyle.Fill };
    readonly Label versionLabel = new() { AutoSize = true, Dock = DockStyle.Right };
    readonly ProgressBar activity = new() { Style = ProgressBarStyle.Marquee, Visible = false, Dock = DockStyle.Right };

    CancellationTokenSource? scanCts;
    IReadOnlyList<CleanupGroup> groups = [];
    bool updatingChecks;
    bool shuttingDown;

    public CleanupPage()
    {
        hint.Height = LogicalToDeviceUnits(56);

        tree.ImageList = icons;
        tree.AfterCheck += OnAfterCheck;
        tree.BeforeExpand += OnBeforeExpand;

        buttonRow.Dock = DockStyle.Top;
        buttonRow.AutoSize = true;
        buttonRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        buttonRow.WrapContents = false;
        buttonRow.Margin = new Padding(0);
        buttonRow.Padding = new Padding(0, 0, 0, 12);
        buttonRow.Controls.Add(scanButton);
        buttonRow.Controls.Add(runButton);

        activity.Width = LogicalToDeviceUnits(140);
        activity.Height = LogicalToDeviceUnits(6);
        versionLabel.Font = new Font("Segoe UI", 7.5f);
        versionLabel.TextAlign = ContentAlignment.MiddleRight;
        versionLabel.Text = "v" + Humanize.Version;
        statusBar.Dock = DockStyle.Bottom;
        statusBar.Height = LogicalToDeviceUnits(26);
        statusBar.Padding = new Padding(2, 6, 2, 0);
        statusBar.Controls.Add(status);
        statusBar.Controls.Add(activity);
        statusBar.Controls.Add(versionLabel);

        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(14, 12, 14, 12);
        card.Controls.Add(tree);

        scanButton.Click += async (_, _) => await ScanAsync();
        runButton.Click += async (_, _) => await RunAsync();
        runButton.Enabled = false;

        Controls.Add(card);
        Controls.Add(statusBar);
        Controls.Add(buttonRow);
        Controls.Add(hint);
    }

    public override void ApplyLanguage()
    {
        hint.Text = Strings.Get(UiText.CleanupHint) + " " + Strings.Get(UiText.CleanupRecycleNote)
            + "\n" + Strings.Get(UiText.CleanupExpandHint);
        scanButton.Text = Strings.Get(UiText.CleanupScanButton);
        runButton.Text = Strings.Get(UiText.CleanupRunButton);
        Populate();
        UpdateStatus();
    }

    public override void ApplyTheme(Palette palette)
    {
        BackColor = palette.Background;
        hint.BackColor = palette.Background;
        hint.ForeColor = palette.MutedText;
        statusBar.BackColor = palette.Background;
        status.BackColor = palette.Background;
        status.ForeColor = palette.MutedText;
        versionLabel.BackColor = palette.Background;
        versionLabel.ForeColor = Color.FromArgb(120, palette.MutedText);
        activity.BackColor = palette.Background;
        buttonRow.BackColor = palette.Background;
        card.Fill = palette.Surface;
        card.Edge = palette.Border;
        tree.BackColor = palette.Surface;
        tree.ForeColor = palette.Text;
        tree.LineColor = palette.Border;
        Skin(scanButton, palette.Accent, palette.AccentText, palette);
        Skin(runButton, palette.Danger, palette.DangerText, palette);
        tree.Invalidate();
    }

    async Task ScanAsync()
    {
        scanCts?.Dispose();
        var currentCts = new CancellationTokenSource();
        scanCts = currentCts;
        var token = currentCts.Token;

        scanButton.Enabled = false;
        runButton.Enabled = false;
        activity.Visible = true;
        Busy = true;
        status.Text = Strings.Get(UiText.CleanupScanning);
        tree.Nodes.Clear();

        try
        {
            groups = await Task.Run(() => Cleanup.BuildPlan(token), token);
            Populate();
        }
        catch (OperationCanceledException)
        {
            groups = [];
        }
        finally
        {
            if (ReferenceEquals(scanCts, currentCts)) scanCts = null;
            currentCts.Dispose();
            if (!IsDisposed && !Disposing)
            {
                scanButton.Enabled = true;
                activity.Visible = false;
                Busy = false;
                UpdateStatus();
            }
        }
    }

    void Populate()
    {
        updatingChecks = true;
        tree.BeginUpdate();
        tree.Nodes.Clear();

        foreach (var group in groups)
        {
            var node = new TreeNode($"{Strings.Get(group.Name)}   ·   {Humanize.Bytes(group.TotalBytes)}   ·   {Humanize.Number(group.Items.Count)}")
            {
                Tag = group,
                Checked = true,
                ToolTipText = group.Root
            };

            node.Nodes.Add(new TreeNode("...") { Tag = "pending" });
            tree.Nodes.Add(node);
        }

        tree.EndUpdate();
        updatingChecks = false;
    }

    void OnBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node is null || e.Node.Tag is not CleanupGroup group) return;
        if (e.Node.Nodes.Count != 1 || e.Node.Nodes[0].Tag as string != "pending") return;

        updatingChecks = true;
        tree.BeginUpdate();
        e.Node.Nodes.Clear();

        foreach (var item in group.Items.OrderByDescending(entry => entry.Size).Take(MaxItemsPerGroup))
        {
            var name = Path.GetFileName(item.FullPath);
            if (name.Length == 0) name = item.FullPath;
            var child = new TreeNode($"{name}   ·   {Humanize.Bytes(item.Size)}")
            {
                Tag = item,
                Checked = e.Node.Checked,
                ToolTipText = item.FullPath
            };

            var index = IconIndex(item, name);
            child.ImageIndex = index;
            child.SelectedImageIndex = index;
            e.Node.Nodes.Add(child);
        }

        if (group.Items.Count > MaxItemsPerGroup)
        {
            e.Node.Nodes.Add(new TreeNode($"+ {Humanize.Number(group.Items.Count - MaxItemsPerGroup)}")
            {
                Tag = "overflow",
                Checked = e.Node.Checked,
                ForeColor = Theme.Current.MutedText
            });
        }

        tree.EndUpdate();
        updatingChecks = false;
    }

    int IconIndex(CleanupItem item, string name)
    {
        var key = item.IsDirectory ? "<dir>" : IconKey(name);
        if (iconIndex.TryGetValue(key, out var cached)) return cached;

        var image = item.IsDirectory ? Icons.ForFolder() : Icons.ForFileName(name);
        if (image is null)
        {
            iconIndex[key] = -1;
            return -1;
        }

        icons.Images.Add(image);
        iconIndex[key] = icons.Images.Count - 1;
        return iconIndex[key];
    }

    static string IconKey(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "<none>" : name[dot..].ToLowerInvariant();
    }

    void OnAfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (updatingChecks || e.Node is null) return;

        updatingChecks = true;
        if (e.Node.Parent is null)
        {
            foreach (TreeNode child in e.Node.Nodes) child.Checked = e.Node.Checked;
        }
        else
        {
            var parent = e.Node.Parent;
            var anyChecked = parent.Nodes.Cast<TreeNode>().Any(node => node.Checked);
            parent.Checked = anyChecked;
        }

        updatingChecks = false;
        UpdateStatus();
    }

    List<CleanupItem> SelectedItems()
    {
        var selected = new List<CleanupItem>();
        foreach (TreeNode node in tree.Nodes)
        {
            if (node.Tag is not CleanupGroup group) continue;

            var expanded = node.Nodes.Count > 0 && node.Nodes[0].Tag as string != "pending";
            if (!expanded)
            {
                if (node.Checked) selected.AddRange(group.Items);
                continue;
            }

            var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var overflowChecked = false;
            foreach (TreeNode child in node.Nodes)
            {
                if (child.Tag is CleanupItem item)
                {
                    shown.Add(item.FullPath);
                    if (child.Checked) chosen.Add(item.FullPath);
                }
                else if (child.Tag as string == "overflow" && child.Checked)
                {
                    overflowChecked = true;
                }
            }

            foreach (var item in group.Items)
            {
                if (chosen.Contains(item.FullPath))
                {
                    selected.Add(item);
                    continue;
                }

                if (!shown.Contains(item.FullPath) && overflowChecked) selected.Add(item);
            }
        }

        return selected;
    }

    async Task RunAsync()
    {
        var items = SelectedItems();
        if (items.Count == 0) return;

        var total = items.Sum(item => item.Size);
        var answer = MessageBox.Show(
            Strings.Format(UiText.CleanupConfirmFormat, Humanize.Number(items.Count), Humanize.Bytes(total)),
            Strings.Get(UiText.CleanupConfirmTitle),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        scanButton.Enabled = false;
        runButton.Enabled = false;
        activity.Visible = true;
        Busy = true;

        try
        {
            var outcome = await Task.Run(() => Cleanup.Execute(items));
            if (shuttingDown || IsDisposed || Disposing) return;
            status.Text = Strings.Format(UiText.CleanupDoneFormat,
                Humanize.Number(outcome.Removed), Humanize.Number(outcome.Skipped));
            groups = [];
            tree.Nodes.Clear();
        }
        finally
        {
            if (!shuttingDown && !IsDisposed && !Disposing)
            {
                scanButton.Enabled = true;
                activity.Visible = false;
                Busy = false;
            }
        }
    }

    void UpdateStatus()
    {
        if (tree.Nodes.Count == 0)
        {
            runButton.Enabled = false;
            status.Text = groups.Count == 0 && scanButton.Enabled
                ? Strings.Get(UiText.CleanupNothing)
                : Strings.Get(UiText.Ready);
            return;
        }

        var selected = SelectedItems();
        var available = groups.Sum(group => group.Items.Count);
        runButton.Enabled = selected.Count > 0;
        status.Text = Strings.Format(UiText.CleanupSelectedFormat,
            Humanize.Number(selected.Count), Humanize.Number(available), Humanize.Bytes(selected.Sum(item => item.Size)));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelWork();
            scanCts = null;
            icons.Dispose();
        }

        base.Dispose(disposing);
    }

    public override void CancelWork()
    {
        shuttingDown = true;
        scanCts?.Cancel();
    }
}
