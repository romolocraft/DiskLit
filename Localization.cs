using System.Diagnostics;
using System.Globalization;

namespace DiskLit;

internal enum Language
{
    English,
    Portuguese,
    Spanish,
    Russian
}

internal enum UiText
{
    AppTitle,
    Headline,
    Intro,
    Analyze,
    Stop,
    FilterPlaceholder,
    AllFolders,
    FolderLabel,
    ColumnName,
    ColumnSize,
    ColumnLocation,
    ColumnModified,
    OpenLocation,
    CopyPath,
    Ready,
    ScanningHint,
    ScanStopped,
    ScanFailedPrefix,
    SolidState,
    Sequential,
    RootFolder,
    CouldNotOpenTitle,
    CouldNotCopyTitle,
    ProgressFormat,
    SummaryFormat,
    DoneFormat,
    DriveFormat,
    UnknownDate,
    ThemeSystem,
    ThemeLight,
    ThemeDark
}

internal static class Strings
{
    static string[] table = Table(Language.English);

    public static Language Current { get; private set; } = Language.English;

    static Strings()
    {
        var expected = Enum.GetValues<UiText>().Length;
        foreach (var language in Enum.GetValues<Language>())
            Debug.Assert(Table(language).Length == expected, $"{language} table has wrong length");
    }

    public static string Get(UiText id) => table[(int)id];

    public static void UseSystemLanguage() => Use(DetectSystemLanguage());

    public static void Use(Language language)
    {
        Current = language;
        table = Table(language);
    }

    public static Language DetectSystemLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "pt" => Language.Portuguese,
            "es" => Language.Spanish,
            "ru" => Language.Russian,
            _ => Language.English
        };

    static string[] Table(Language language) => language switch
    {
        Language.Portuguese =>
        [
            "DiskLit — Analisador de espaço em disco",
            "Descubra o que está ocupando seu disco",
            "Selecione um disco. A análise apenas lê os arquivos e não apaga nada.",
            "Analisar",
            "Parar",
            "Filtrar entre os maiores arquivos...",
            "Todas as pastas",
            "Pasta:",
            "Nome",
            "Tamanho",
            "Localização",
            "Modificado",
            "Abrir localização",
            "Copiar caminho",
            "Pronto",
            "Analisando em segundo plano; você pode parar a qualquer momento.",
            "Análise interrompida.",
            "Não foi possível concluir: ",
            "SSD/NVMe",
            "modo sequencial",
            "(raiz)",
            "Não foi possível abrir",
            "Não foi possível copiar",
            "{0} arquivos • {1} lidos • {2} inacessíveis • {3}",
            "{0} arquivos • {1} • {2} • {3} • {4} inacessíveis",
            "Concluído. Exibindo os {0} maiores arquivos; o filtro busca apenas entre eles. Clique duas vezes para abrir a localização.",
            "{0}  {1}  •  {2} de {3}",
            "—",
            "Tema: sistema",
            "Tema: claro",
            "Tema: escuro"
        ],
        Language.Spanish =>
        [
            "DiskLit — Analizador de espacio en disco",
            "Descubra qué está ocupando su disco",
            "Seleccione un disco. El análisis solo lee los archivos y no elimina nada.",
            "Analizar",
            "Detener",
            "Filtrar entre los archivos más grandes...",
            "Todas las carpetas",
            "Carpeta:",
            "Nombre",
            "Tamaño",
            "Ubicación",
            "Modificado",
            "Abrir ubicación",
            "Copiar ruta",
            "Listo",
            "Analizando en segundo plano; puede detenerlo en cualquier momento.",
            "Análisis interrumpido.",
            "No se pudo completar: ",
            "SSD/NVMe",
            "modo secuencial",
            "(raíz)",
            "No se pudo abrir",
            "No se pudo copiar",
            "{0} archivos • {1} leídos • {2} inaccesibles • {3}",
            "{0} archivos • {1} • {2} • {3} • {4} inaccesibles",
            "Listo. Mostrando los {0} archivos más grandes; el filtro busca solo entre ellos. Haga doble clic para abrir la ubicación.",
            "{0}  {1}  •  {2} de {3}",
            "—",
            "Tema: sistema",
            "Tema: claro",
            "Tema: oscuro"
        ],
        Language.Russian =>
        [
            "DiskLit — Анализатор дискового пространства",
            "Узнайте, что занимает место на диске",
            "Выберите диск. Анализ только читает файлы и ничего не удаляет.",
            "Анализировать",
            "Остановить",
            "Поиск среди крупнейших файлов...",
            "Все папки",
            "Папка:",
            "Имя",
            "Размер",
            "Расположение",
            "Изменён",
            "Открыть расположение файла",
            "Копировать путь",
            "Готово",
            "Анализ выполняется в фоне; его можно остановить в любой момент.",
            "Анализ прерван.",
            "Не удалось завершить: ",
            "SSD/NVMe",
            "последовательный режим",
            "(корень)",
            "Не удалось открыть",
            "Не удалось скопировать",
            "{0} файлов • {1} прочитано • {2} недоступно • {3}",
            "{0} файлов • {1} • {2} • {3} • {4} недоступно",
            "Готово. Показаны {0} крупнейших файлов; поиск работает только по ним. Дважды щёлкните, чтобы открыть расположение.",
            "{0}  {1}  •  {2} из {3}",
            "—",
            "Тема: системная",
            "Тема: светлая",
            "Тема: тёмная"
        ],
        _ =>
        [
            "DiskLit — Disk Space Analyzer",
            "See what is taking up your disk",
            "Select a drive. The scan only reads files and deletes nothing.",
            "Analyze",
            "Stop",
            "Filter among the largest files...",
            "All folders",
            "Folder:",
            "Name",
            "Size",
            "Location",
            "Modified",
            "Open file location",
            "Copy path",
            "Ready",
            "Scanning in the background; you can stop at any time.",
            "Scan stopped.",
            "Could not finish: ",
            "SSD/NVMe",
            "sequential mode",
            "(root)",
            "Could not open",
            "Could not copy",
            "{0} files • {1} read • {2} inaccessible • {3}",
            "{0} files • {1} • {2} • {3} • {4} inaccessible",
            "Done. Showing the {0} largest files; the filter searches only among them. Double-click to open the location.",
            "{0}  {1}  •  {2} of {3}",
            "—",
            "Theme: system",
            "Theme: light",
            "Theme: dark"
        ]
    };
}
