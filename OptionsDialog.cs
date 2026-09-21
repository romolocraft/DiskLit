namespace DiskLit;

internal sealed class OptionsDialog : Form
{
    readonly Settings settings;
    readonly TableLayoutPanel layout = new();
    readonly Label languageLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    readonly Label themeLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    readonly Label searchLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    readonly DropDown languageBox = new();
    readonly DropDown themeBox = new();
    readonly DropDown searchBox = new();
    readonly ThemedButton closeButton = new();
    readonly Label pathLabel = new() { AutoSize = false, Dock = DockStyle.Bottom, AutoEllipsis = true };

    public event EventHandler? Changed;

    public OptionsDialog(Settings settings)
    {
        this.settings = settings;

        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(420, 230);
        Icon = Assets.AppIcon;

        BuildUi();
        Fill();
        ApplyLanguage();
        ApplyTheme();
        Wire();
    }

    void BuildUi()
    {
        foreach (var box in new[] { languageBox, themeBox, searchBox })
        {
            box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            box.Margin = new Padding(0, 4, 0, 10);
            box.ItemHeight = 24;
            box.Height = 30;
        }

        closeButton.AutoSize = true;
        closeButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        closeButton.Padding = new Padding(18, 6, 18, 6);



        closeButton.Anchor = AnchorStyles.Right;
        closeButton.Margin = new Padding(0, 6, 0, 0);

        pathLabel.Height = 34;
        pathLabel.Font = new Font("Segoe UI", 7.5f);
        pathLabel.Padding = new Padding(20, 0, 20, 8);

        layout.Dock = DockStyle.Fill;
        layout.Padding = new Padding(20, 18, 20, 8);
        layout.ColumnCount = 2;
        layout.RowCount = 4;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 4; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        languageLabel.Margin = new Padding(0, 8, 14, 10);
        themeLabel.Margin = new Padding(0, 8, 14, 10);
        searchLabel.Margin = new Padding(0, 8, 14, 10);

        layout.Controls.Add(languageLabel, 0, 0);
        layout.Controls.Add(languageBox, 1, 0);
        layout.Controls.Add(themeLabel, 0, 1);
        layout.Controls.Add(themeBox, 1, 1);
        layout.Controls.Add(searchLabel, 0, 2);
        layout.Controls.Add(searchBox, 1, 2);
        layout.Controls.Add(closeButton, 1, 3);

        Controls.Add(layout);
        Controls.Add(pathLabel);
        AcceptButton = closeButton;
        CancelButton = closeButton;
    }

    void Fill()
    {
        languageBox.Add(new Choice<Language?>(null, Strings.Get(UiText.LanguageAuto)));
        languageBox.Add(new Choice<Language?>(Language.English, "English"));
        languageBox.Add(new Choice<Language?>(Language.Portuguese, "Português"));
        languageBox.Add(new Choice<Language?>(Language.Spanish, "Español"));
        languageBox.Add(new Choice<Language?>(Language.Russian, "Русский"));
        languageBox.SelectedIndex = settings.Language switch
        {
            DiskLit.Language.English => 1,
            DiskLit.Language.Portuguese => 2,
            DiskLit.Language.Spanish => 3,
            DiskLit.Language.Russian => 4,
            _ => 0
        };

        themeBox.Add(new Choice<ThemeMode>(ThemeMode.System, Strings.Get(UiText.ThemeOptionSystem)));
        themeBox.Add(new Choice<ThemeMode>(ThemeMode.Light, Strings.Get(UiText.ThemeOptionLight)));
        themeBox.Add(new Choice<ThemeMode>(ThemeMode.Dark, Strings.Get(UiText.ThemeOptionDark)));
        themeBox.SelectedIndex = settings.Theme switch
        {
            ThemeMode.Light => 1,
            ThemeMode.Dark => 2,
            _ => 0
        };

        foreach (var engine in Enum.GetValues<SearchEngine>())
            searchBox.Add(new Choice<SearchEngine>(engine, Browsers.Label(engine)));
        searchBox.SelectedIndex = (int)settings.Search;

    }

    void Wire()
    {
        languageBox.SelectedIndexChanged += (_, _) =>
        {
            if (languageBox.SelectedItem is not Choice<Language?> choice) return;
            settings.Language = choice.Value;
            Commit();
            ApplyLanguage();
        };

        themeBox.SelectedIndexChanged += (_, _) =>
        {
            if (themeBox.SelectedItem is not Choice<ThemeMode> choice) return;
            settings.Theme = choice.Value;
            Commit();
            ApplyTheme();
        };

        searchBox.SelectedIndexChanged += (_, _) =>
        {
            if (searchBox.SelectedItem is not Choice<SearchEngine> choice) return;
            settings.Search = choice.Value;
            Commit();
        };


        closeButton.Click += (_, _) => Close();
    }

    void Commit()
    {
        settings.Apply();
        settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void ApplyLanguage()
    {
        Text = Strings.Get(UiText.OptionsTitle);
        languageLabel.Text = Strings.Get(UiText.OptionsLanguage);
        themeLabel.Text = Strings.Get(UiText.OptionsTheme);
        searchLabel.Text = Strings.Get(UiText.OptionsSearch);
        closeButton.Text = Strings.Get(UiText.OptionsClose);
        pathLabel.Text = Settings.FilePath;

        if (languageBox.Items.Count > 0)
            languageBox.Replace(0, new Choice<Language?>(null, Strings.Get(UiText.LanguageAuto)));
        if (themeBox.Items.Count >= 3)
        {
            themeBox.Replace(0, new Choice<ThemeMode>(ThemeMode.System, Strings.Get(UiText.ThemeOptionSystem)));
            themeBox.Replace(1, new Choice<ThemeMode>(ThemeMode.Light, Strings.Get(UiText.ThemeOptionLight)));
            themeBox.Replace(2, new Choice<ThemeMode>(ThemeMode.Dark, Strings.Get(UiText.ThemeOptionDark)));
        }
    }

    void ApplyTheme()
    {
        var palette = Theme.Current;
        BackColor = palette.Background;
        ForeColor = palette.Text;
        layout.BackColor = palette.Background;
        pathLabel.BackColor = palette.Background;
        pathLabel.ForeColor = Color.FromArgb(140, palette.MutedText);

        foreach (var label in new[] { languageLabel, themeLabel, searchLabel })
        {
            label.BackColor = palette.Background;
            label.ForeColor = palette.Text;
        }

        foreach (var box in new[] { languageBox, themeBox, searchBox })
        {
            box.BackColor = palette.Surface;
            box.ForeColor = palette.Text;
            box.Invalidate();
        }

        closeButton.Recolor(palette.Accent, palette.AccentText, palette);

        if (IsHandleCreated) Theme.ApplyTitleBar(this);
        Invalidate(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this);
    }

    sealed class Choice<T>(T value, string label)
    {
        public T Value { get; } = value;

        public override string ToString() => label;
    }
}
