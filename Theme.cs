using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DiskLit;

internal enum ThemeMode
{
    System,
    Light,
    Dark
}

internal sealed record Palette(
    Color Background,
    Color Surface,
    Color Text,
    Color MutedText,
    Color Accent,
    Color AccentText,
    Color Subtle,
    Color SubtleText,
    Color Border,
    Color Selection,
    Color MenuHighlight,
    Color Rail,
    Color RailSelected,
    Color Notice,
    Color NoticeBorder,
    Color Danger,
    Color DangerText);

internal static class Theme
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string AppsUseLightTheme = "AppsUseLightTheme";
    const int UseImmersiveDarkMode = 20;

    public static readonly Palette Light = new(
        Background: Color.FromArgb(245, 247, 250),
        Surface: Color.White,
        Text: Color.FromArgb(28, 35, 48),
        MutedText: Color.FromArgb(92, 101, 116),
        Accent: Color.FromArgb(37, 99, 235),
        AccentText: Color.White,
        Subtle: Color.FromArgb(226, 232, 240),
        SubtleText: Color.FromArgb(45, 55, 72),
        Border: Color.FromArgb(214, 221, 231),
        Selection: Color.FromArgb(219, 234, 254),
        MenuHighlight: Color.FromArgb(232, 240, 254),
        Rail: Color.FromArgb(236, 240, 246),
        RailSelected: Color.FromArgb(255, 255, 255),
        Notice: Color.FromArgb(255, 247, 224),
        NoticeBorder: Color.FromArgb(234, 199, 108),
        Danger: Color.FromArgb(194, 46, 46),
        DangerText: Color.White);

    public static readonly Palette Dark = new(
        Background: Color.FromArgb(20, 23, 28),
        Surface: Color.FromArgb(27, 31, 38),
        Text: Color.FromArgb(230, 233, 239),
        MutedText: Color.FromArgb(154, 164, 178),
        Accent: Color.FromArgb(59, 130, 246),
        AccentText: Color.White,
        Subtle: Color.FromArgb(42, 48, 57),
        SubtleText: Color.FromArgb(230, 233, 239),
        Border: Color.FromArgb(52, 59, 70),
        Selection: Color.FromArgb(30, 58, 95),
        MenuHighlight: Color.FromArgb(42, 48, 57),
        Rail: Color.FromArgb(16, 19, 24),
        RailSelected: Color.FromArgb(33, 39, 48),
        Notice: Color.FromArgb(46, 40, 24),
        NoticeBorder: Color.FromArgb(122, 96, 40),
        Danger: Color.FromArgb(208, 74, 74),
        DangerText: Color.White);

    public static ThemeMode Mode { get; private set; } = ThemeMode.System;

    public static bool IsDark { get; private set; }

    public static Palette Current { get; private set; } = Light;

    public static void Use(ThemeMode mode)
    {
        Mode = mode;
        IsDark = mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            _ => SystemPrefersDark()
        };
        Current = IsDark ? Dark : Light;
    }

    public static void Refresh() => Use(Mode);

    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(AppsUseLightTheme) is int value && value == 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public static void ApplyTitleBar(IWin32Window window)
    {
        var dark = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(window.Handle, UseImmersiveDarkMode, ref dark, sizeof(int));
    }

    public static void ApplyNativeListStyle(ListView list) =>
        _ = SetWindowTheme(list.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);

    public static void ApplyNativeTreeStyle(TreeView tree) =>
        _ = SetWindowTheme(tree.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int SetWindowTheme(IntPtr window, string? applicationName, string? subIdList);
}

internal sealed class ThemedMenuRenderer(Palette palette) : ToolStripProfessionalRenderer(new ThemedColorTable(palette))
{
    readonly Palette palette = palette;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item?.Enabled == true ? palette.Text : palette.MutedText;
        base.OnRenderItemText(e);
    }
}

internal sealed class ThemedColorTable(Palette palette) : ProfessionalColorTable
{
    readonly Palette palette = palette;

    public override Color ToolStripDropDownBackground => palette.Surface;

    public override Color ImageMarginGradientBegin => palette.Surface;

    public override Color ImageMarginGradientMiddle => palette.Surface;

    public override Color ImageMarginGradientEnd => palette.Surface;

    public override Color MenuItemSelected => palette.MenuHighlight;

    public override Color MenuItemSelectedGradientBegin => palette.MenuHighlight;

    public override Color MenuItemSelectedGradientEnd => palette.MenuHighlight;

    public override Color MenuItemBorder => palette.Border;

    public override Color MenuBorder => palette.Border;

    public override Color SeparatorDark => palette.Border;

    public override Color SeparatorLight => palette.Border;
}
