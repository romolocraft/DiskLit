using System.ComponentModel;
using System.Diagnostics;

namespace DiskLit;

internal enum SearchEngine
{
    Google,
    Bing,
    DuckDuckGo,
    Brave,
    Ecosia
}

internal static class Browsers
{
    public static SearchEngine Engine { get; set; } = SearchEngine.Google;

    public static string Label(SearchEngine engine) => engine switch
    {
        SearchEngine.Bing => "Bing",
        SearchEngine.DuckDuckGo => "DuckDuckGo",
        SearchEngine.Brave => "Brave Search",
        SearchEngine.Ecosia => "Ecosia",
        _ => "Google"
    };

    static string Template(SearchEngine engine) => engine switch
    {
        SearchEngine.Bing => "https://www.bing.com/search?q=",
        SearchEngine.DuckDuckGo => "https://duckduckgo.com/?q=",
        SearchEngine.Brave => "https://search.brave.com/search?q=",
        SearchEngine.Ecosia => "https://www.ecosia.org/search?q=",
        _ => "https://www.google.com/search?q="
    };

    public static bool Search(string query)
    {
        var url = BuildSearchUrl(query, Engine);
        if (url is null) return false;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    public static string? BuildSearchUrl(string query) => BuildSearchUrl(query, Engine);

    public static string? BuildSearchUrl(string query, SearchEngine engine)
    {
        var text = Sanitize(query);
        return text.Length == 0 ? null : Template(engine) + Uri.EscapeDataString(text);
    }

    static string Sanitize(string query)
    {
        var cleaned = query.Replace('"', ' ').Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        while (cleaned.Contains("  ", StringComparison.Ordinal))
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        return cleaned;
    }
}
