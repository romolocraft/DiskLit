using System.Runtime.InteropServices;

namespace DiskLit;

internal sealed class MainForm : Form
{

    const int WmSettingChange = 0x001A;
    const int WmDeviceChange = 0x0219;

    readonly Panel rail = new();
    readonly Panel brand = new();
    readonly PictureBox brandIcon = new() { SizeMode = PictureBoxSizeMode.Zoom };
    readonly Label brandName = new() { AutoSize = true };
    readonly Panel host = new();
    readonly ThemedButton themeButton = new();
    readonly GearButton optionsButton = new();
    readonly ToolTip optionsTip = new();
    readonly TableLayoutPanel railBottom = new();
    readonly Settings settings;
    readonly NavButton[] navButtons;
    readonly Page[] pages;
    readonly UiText[] navLabels = [UiText.NavFiles, UiText.NavActive, UiText.NavCleanup];
    readonly System.Windows.Forms.Timer busyTimer = new() { Interval = 40 };
    readonly System.Windows.Forms.Timer closeTimer = new() { Interval = 100 };
    int current = -1;

    void SyncBusy(int index)
    {
        var button = navButtons[index];
        button.Busy = pages[index].Busy;
        button.Invalidate();
        if (button.Busy && !busyTimer.Enabled) busyTimer.Start();
    }

    public MainForm(Settings settings)
    {
        this.settings = settings;

        AutoScaleMode = AutoScaleMode.Font;
        MinimumSize = new Size(940, 620);
        Size = new Size(1220, 780);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        DoubleBuffered = true;
        Icon = Assets.AppIcon;

        pages = [new FilesPage(), new ActivePage(), new CleanupPage()];
        closeTimer.Tick += (_, _) =>
        {
            if (pages.OfType<CleanupPage>().Any(page => page.IsCleaning)) return;
            closeTimer.Stop();
            Close();
        };
        navButtons = new NavButton[pages.Length];

        BuildUi();
        ApplyLanguage();
        ApplyTheme();
        Select(0);
    }

    void BuildUi()
    {
        host.Dock = DockStyle.Fill;
        foreach (var page in pages) host.Controls.Add(page);

        brandName.Font = new Font("Segoe UI Semibold", 13);
        brandName.Text = "DiskLit";
        brandIcon.Size = new Size(LogicalToDeviceUnits(26), LogicalToDeviceUnits(26));
        brandIcon.Location = new Point(LogicalToDeviceUnits(16), LogicalToDeviceUnits(18));
        brandName.Location = new Point(LogicalToDeviceUnits(50), LogicalToDeviceUnits(21));
        brand.Dock = DockStyle.Top;
        brand.Height = LogicalToDeviceUnits(70);
        brand.Controls.Add(brandIcon);
        brand.Controls.Add(brandName);

        for (var index = pages.Length - 1; index >= 0; index--)
        {
            var slot = index;
            var button = new NavButton();
            button.Click += (_, _) => Select(slot);
            navButtons[index] = button;
            rail.Controls.Add(button);
            pages[slot].BusyChanged += (_, _) => SyncBusy(slot);
        }

        busyTimer.Tick += (_, _) =>
        {
            var anyBusy = false;
            foreach (var button in navButtons)
            {
                if (!button.Busy) continue;
                anyBusy = true;
                button.BusyPhase += 9;
                button.Invalidate();
            }

            if (!anyBusy) busyTimer.Stop();
        };

        foreach (var button in new[] { themeButton, optionsButton })
        {
            button.Height = LogicalToDeviceUnits(36);
            button.Dock = DockStyle.Fill;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(18, 0, 8, 0);
            button.Margin = new Padding(0);
        }

        optionsButton.Padding = Padding.Empty;
        optionsButton.TextAlign = ContentAlignment.MiddleCenter;

        themeButton.Click += (_, _) => CycleTheme();
        optionsButton.Click += (_, _) => ShowOptions();

        railBottom.Dock = DockStyle.Bottom;
        railBottom.AutoSize = true;
        railBottom.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        railBottom.ColumnCount = 1;
        railBottom.RowCount = 2;
        railBottom.Margin = new Padding(0);
        railBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        railBottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        railBottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        railBottom.Controls.Add(themeButton, 0, 0);
        railBottom.Controls.Add(optionsButton, 0, 1);

        rail.Dock = DockStyle.Left;
        rail.Width = LogicalToDeviceUnits(206);
        rail.Controls.Add(railBottom);
        rail.Controls.Add(brand);

        Controls.Add(host);
        Controls.Add(rail);
    }

    void Select(int index)
    {
        if (index == current) return;
        if (current >= 0)
        {
            pages[current].Deactivated();
            pages[current].Visible = false;
            navButtons[current].Active = false;
        }

        current = index;
        navButtons[index].Active = true;
        pages[index].Visible = true;
        pages[index].BringToFront();
        pages[index].Activated();
    }

    void ApplyLanguage()
    {
        Text = Strings.Get(UiText.AppTitle);
        for (var index = 0; index < navButtons.Length; index++)
            navButtons[index].Text = "   " + Strings.Get(navLabels[index]);
        ApplyThemeLabel();
        optionsButton.Text = "";
        optionsButton.AccessibleName = Strings.Get(UiText.OptionsTitle);
        optionsTip.SetToolTip(optionsButton, Strings.Get(UiText.OptionsTitle));
        foreach (var page in pages) page.ApplyLanguage();
    }

    void CycleTheme()
    {
        settings.Theme = Theme.Mode switch
        {
            ThemeMode.System => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.Dark,
            _ => ThemeMode.System
        };
        settings.Apply();
        settings.Save();
        ApplyThemeLabel();
        ApplyTheme();
    }

    void ShowOptions()
    {
        using var dialog = new OptionsDialog(settings);
        dialog.Changed += (_, _) =>
        {
            ApplyLanguage();
            ApplyTheme();
        };
        dialog.ShowDialog(this);
    }

    void ApplyThemeLabel() => themeButton.Text = Strings.Get(Theme.Mode switch
    {
        ThemeMode.Light => UiText.ThemeLight,
        ThemeMode.Dark => UiText.ThemeDark,
        _ => UiText.ThemeSystem
    });

    void ApplyTheme()
    {
        var palette = Theme.Current;
        SuspendLayout();
        try
        {
            BackColor = palette.Background;
            ForeColor = palette.Text;
            host.BackColor = palette.Background;
            rail.BackColor = palette.Rail;
            brand.BackColor = palette.Rail;
            brandName.BackColor = palette.Rail;
            brandName.ForeColor = palette.Text;
            brandIcon.BackColor = palette.Rail;
            brandIcon.Image = Assets.Glyph(brandIcon.Width, palette.Accent);

            foreach (var button in navButtons)
            {
                button.Palette = palette;
                button.Restyle();
                button.Invalidate();
            }

            railBottom.BackColor = palette.Rail;
            foreach (var button in new[] { themeButton, optionsButton })
                button.RecolorRail(palette, false);

            foreach (var page in pages) page.ApplyTheme(palette);

            if (IsHandleCreated) Theme.ApplyTitleBar(this);
        }
        finally
        {
            ResumeLayout(false);
            Invalidate(true);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this);
        foreach (var page in pages) page.ApplyTheme(Theme.Current);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            busyTimer.Dispose();
            closeTimer.Dispose();
            optionsTip.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        foreach (var page in pages) page.CancelWork();
        busyTimer.Stop();
        if (pages.OfType<CleanupPage>().Any(page => page.IsCleaning))
        {
            e.Cancel = true;
            Enabled = false;
            closeTimer.Start();
        }
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WmDeviceChange)
        {
            if (pages.Length > 0 && pages[0] is FilesPage files) files.NotifyStorageChanged();
            return;
        }
        if (m.Msg != WmSettingChange || Theme.Mode != ThemeMode.System) return;
        var section = m.LParam == IntPtr.Zero ? null : Marshal.PtrToStringUni(m.LParam);
        if (section is not "ImmersiveColorSet") return;
        var wasDark = Theme.IsDark;
        Theme.Refresh();
        if (Theme.IsDark != wasDark) ApplyTheme();
    }
}
