using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DiskLit;

internal sealed class MainForm : Form
{
    readonly ComboBox driveBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Button scanButton = new() { Text = "Analisar" };
    readonly Button stopButton = new() { Text = "Parar", Enabled = false };
    readonly TextBox searchBox = new() { PlaceholderText = "Filtrar entre os maiores arquivos..." };
    readonly Label headline = new() { Text = "Descubra o que está ocupando seu disco", AutoSize = true };
    readonly Label summary = new() { AutoSize = true };
    readonly Label status = new() { AutoEllipsis = true };
    readonly ProgressBar activity = new() { Style = ProgressBarStyle.Marquee, Visible = false, MarqueeAnimationSpeed = 24 };
    readonly ListView list = new() { View = View.Details, FullRowSelect = true, GridLines = false, VirtualMode = true };
    readonly ContextMenuStrip menu = new();
    readonly System.Windows.Forms.Timer filterTimer = new() { Interval = 220 };
    CancellationTokenSource? scanCts;
    List<FileEntry> allFiles = [];
    List<FileEntry> visibleFiles = [];

    public MainForm()
    {
        Text = "DiskLit — Analisador de espaço";
        AutoScaleMode = AutoScaleMode.Font;
        MinimumSize = new Size(820, 560);
        Size = new Size(1120, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(245, 247, 250);
        ForeColor = Color.FromArgb(28, 35, 48);

        BuildUi();
        LoadDrives();
        WireEvents();
    }

    void BuildUi()
    {
        headline.Font = new Font("Segoe UI Semibold", 19);
        headline.Margin = new Padding(0, 0, 0, 2);

        summary.Text = "Selecione um disco. A análise apenas lê os arquivos e não apaga nada.";
        summary.ForeColor = Color.FromArgb(92, 101, 116);
        summary.Margin = new Padding(2, 0, 0, 14);

        driveBox.Width = LogicalToDeviceUnits(310);
        searchBox.Width = LogicalToDeviceUnits(360);
        StyleButton(scanButton, Color.FromArgb(37, 99, 235), Color.White);
        StyleButton(stopButton, Color.FromArgb(226, 232, 240), Color.FromArgb(45, 55, 72));

        var controls = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0)
        };
        controls.Controls.AddRange([driveBox, scanButton, stopButton, searchBox]);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.White,
            Padding = new Padding(22, 18, 22, 14)
        };
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.Controls.Add(headline);
        header.Controls.Add(summary);
        header.Controls.Add(controls);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.White,
            Padding = new Padding(18, 12, 18, 10)
        };
        status.Dock = DockStyle.Fill;
        status.Text = "Pronto";
        status.ForeColor = Color.FromArgb(92, 101, 116);
        activity.Dock = DockStyle.Right;
        activity.Width = LogicalToDeviceUnits(180);
        footer.Controls.Add(status);
        footer.Controls.Add(activity);

        list.Dock = DockStyle.Fill;
        list.BackColor = Color.White;
        list.BorderStyle = BorderStyle.None;
        list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        list.Columns.Add("Nome", LogicalToDeviceUnits(260));
        list.Columns.Add("Tamanho", LogicalToDeviceUnits(120), HorizontalAlignment.Right);
        list.Columns.Add("Localização", LogicalToDeviceUnits(520));
        list.Columns.Add("Modificado", LogicalToDeviceUnits(150));
        list.RetrieveVirtualItem += (_, e) => e.Item = MakeItem(visibleFiles[e.ItemIndex]);

        menu.Items.Add("Abrir localização", null, (_, _) => OpenSelectedLocation());
        menu.Items.Add("Copiar caminho", null, (_, _) => CopySelectedPath());
        list.ContextMenuStrip = menu;

        Controls.Add(list);
        Controls.Add(footer);
        Controls.Add(header);
    }

    static void StyleButton(Button button, Color back, Color fore)
    {
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.Padding = new Padding(14, 5, 14, 5);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = back;
        button.ForeColor = fore;
        button.Cursor = Cursors.Hand;
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
        searchBox.TextChanged += (_, _) => { filterTimer.Stop(); filterTimer.Start(); };
        filterTimer.Tick += (_, _) => { filterTimer.Stop(); ApplyFilter(); };
        list.DoubleClick += (_, _) => OpenSelectedLocation();
        list.Resize += (_, _) => ResizeColumns();
        FormClosing += (_, _) => scanCts?.Cancel();
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
        list.VirtualListSize = 0;
        SetScanning(true);

        var progress = new Progress<ScanProgress>(p =>
        {
            status.Text = $"{FormatNumber(p.Files)} arquivos • {FormatBytes(p.Bytes)} lidos • "
                + $"{FormatNumber(p.Inaccessible)} inacessíveis • {p.Current}";
            if (p.Largest is null) return;
            allFiles = p.Largest;
            ApplyFilter();
        });

        try
        {
            var root = selected.Drive.RootDirectory.FullName;
            var result = await Task.Run(() => NativeScanner.Scan(root, token, progress), token);
            allFiles = result.Largest;
            ApplyFilter();
            var media = result.IsSolidState ? "SSD/NVMe" : "modo sequencial";
            summary.Text = $"{FormatNumber(result.Files)} arquivos • {FormatBytes(result.Bytes)} • "
                + $"{result.Elapsed:mm\\:ss} • {media} • {FormatNumber(result.InaccessibleEntries)} inacessíveis";
            status.Text = $"Concluído. A lista mostra os {FormatNumber(allFiles.Count)} maiores arquivos e o filtro "
                + "busca apenas entre eles; clique duas vezes para abrir a localização.";
        }
        catch (OperationCanceledException)
        {
            status.Text = "Análise interrompida.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            status.Text = "Não foi possível concluir: " + ex.Message;
        }
        finally
        {
            SetScanning(false);
        }
    }

    void ApplyFilter()
    {
        var term = searchBox.Text.Trim();
        visibleFiles = string.IsNullOrEmpty(term)
            ? allFiles
            : allFiles.Where(f => f.FullPath.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        list.VirtualListSize = visibleFiles.Count;
        list.Invalidate();
    }

    void SetScanning(bool scanning)
    {
        scanButton.Enabled = !scanning;
        driveBox.Enabled = !scanning;
        stopButton.Enabled = scanning;
        activity.Visible = scanning;
        if (scanning) summary.Text = "Analisando em segundo plano; você pode parar a qualquer momento.";
    }

    static ListViewItem MakeItem(FileEntry file)
    {
        var item = new ListViewItem(file.Name);
        item.SubItems.Add(FormatBytes(file.Size));
        item.SubItems.Add(Path.GetDirectoryName(file.FullPath) ?? "");
        item.SubItems.Add(file.Modified == DateTime.MinValue ? "—" : file.Modified.ToString("g", CultureInfo.CurrentCulture));
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
            MessageBox.Show(ex.Message, "Não foi possível abrir", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            MessageBox.Show(ex.Message, "Não foi possível copiar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        public override string ToString()
        {
            var used = Drive.TotalSize - Drive.AvailableFreeSpace;
            return $"{Drive.Name}  {Drive.VolumeLabel}  •  {FormatBytes(used)} de {FormatBytes(Drive.TotalSize)}";
        }
    }
}
