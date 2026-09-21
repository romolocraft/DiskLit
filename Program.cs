namespace DiskLit;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        var settings = Settings.Load();
        settings.Apply();

        using var form = new MainForm(settings);
        Application.Run(form);
    }
}
