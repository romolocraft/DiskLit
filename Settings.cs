namespace DiskLit;

internal sealed class Settings
{
    const string FileName = "settings.txt";

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public Language? Language { get; set; }

    public SearchEngine Search { get; set; } = SearchEngine.Google;

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DiskLit");

    public static string FilePath => Path.Combine(Folder, FileName);

    public static Settings Load()
    {
        var settings = new Settings();
        string[] lines;

        try
        {
            if (!File.Exists(FilePath)) return settings;
            lines = File.ReadAllLines(FilePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return settings;
        }

        foreach (var line in lines)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (key.Equals("theme", StringComparison.OrdinalIgnoreCase)
                && TryParseName<ThemeMode>(value, out var theme))
                settings.Theme = theme;
            else if (key.Equals("language", StringComparison.OrdinalIgnoreCase))
                settings.Language = TryParseName<Language>(value, out var language) ? language : null;
            else if (key.Equals("search", StringComparison.OrdinalIgnoreCase)
                && TryParseName<SearchEngine>(value, out var search))
                settings.Search = search;
        }

        return settings;
    }

    static bool TryParseName<T>(string value, out T parsed) where T : struct, Enum
    {
        parsed = default;
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (!candidate.ToString().Equals(value, StringComparison.OrdinalIgnoreCase)) continue;
            parsed = candidate;
            return true;
        }

        return false;
    }

    public void Save()
    {
        var temporary = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(temporary,
            [
                $"theme={Theme}",
                $"language={(Language is null ? "auto" : Language.ToString())}",
                $"search={Search}"
            ]);
            File.Move(temporary, FilePath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException) { }
        }
    }

    public void Apply()
    {
        DiskLit.Theme.Use(Theme);
        if (Language is null) Strings.UseSystemLanguage();
        else Strings.Use(Language.Value);
        Browsers.Engine = Search;
    }
}
