namespace DiskLit;

internal enum FileCategory
{
    Video,
    Images,
    Audio,
    Archives,
    Code,
    Documents,
    System,
    Other
}

internal static class FileTypes
{
    public static readonly FileCategory[] All = Enum.GetValues<FileCategory>();

    static readonly Dictionary<string, FileCategory> ByExtension = Build();

    public static FileCategory Classify(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1) return FileCategory.Other;
        return ByExtension.TryGetValue(name[(dot + 1)..], out var category) ? category : FileCategory.Other;
    }

    public static UiText Label(FileCategory category) => category switch
    {
        FileCategory.Video => UiText.CategoryVideo,
        FileCategory.Images => UiText.CategoryImages,
        FileCategory.Audio => UiText.CategoryAudio,
        FileCategory.Archives => UiText.CategoryArchives,
        FileCategory.Code => UiText.CategoryCode,
        FileCategory.Documents => UiText.CategoryDocuments,
        FileCategory.System => UiText.CategorySystem,
        _ => UiText.CategoryOther
    };

    public static Color Swatch(FileCategory category) => category switch
    {
        FileCategory.Video => Color.FromArgb(79, 70, 229),
        FileCategory.Images => Color.FromArgb(225, 29, 72),
        FileCategory.Audio => Color.FromArgb(168, 85, 247),
        FileCategory.Archives => Color.FromArgb(234, 140, 58),
        FileCategory.Code => Color.FromArgb(61, 155, 143),
        FileCategory.Documents => Color.FromArgb(138, 154, 59),
        FileCategory.System => Color.FromArgb(100, 112, 128),
        _ => Color.FromArgb(176, 184, 196)
    };

    static Dictionary<string, FileCategory> Build()
    {
        var map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);

        Add(FileCategory.Video, "mp4", "mkv", "avi", "mov", "wmv", "flv", "webm", "m4v", "mpg", "mpeg", "ts", "vob", "3gp");
        Add(FileCategory.Images, "jpg", "jpeg", "png", "gif", "bmp", "tif", "tiff", "webp", "svg", "ico", "heic", "raw", "psd", "dds", "tga");
        Add(FileCategory.Audio, "mp3", "wav", "flac", "aac", "ogg", "m4a", "wma", "mid", "midi", "opus", "bank");
        Add(FileCategory.Archives, "zip", "rar", "7z", "tar", "gz", "bz2", "xz", "iso", "cab", "pak", "wad", "bundle", "assets", "resS", "vpk", "gcf");
        Add(FileCategory.Code, "cs", "js", "mjs", "ts", "tsx", "jsx", "py", "java", "cpp", "cc", "c", "h", "hpp", "go", "rs", "rb", "php",
            "html", "htm", "css", "scss", "json", "xml", "yml", "yaml", "sql", "sh", "ps1", "bat", "cmd", "lua", "kt", "swift", "gguf", "safetensors", "onnx", "pt", "ckpt");
        Add(FileCategory.Documents, "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "md", "rtf", "odt", "ods", "csv", "epub", "mobi");
        Add(FileCategory.System, "sys", "dll", "exe", "msi", "dmp", "log", "tmp", "mui", "wim", "etl", "pdb", "cat", "inf", "drv", "ocx", "cpl", "efi", "bin", "dat", "db", "manifest");

        return map;

        void Add(FileCategory category, params string[] extensions)
        {
            foreach (var extension in extensions) map[extension] = category;
        }
    }
}
