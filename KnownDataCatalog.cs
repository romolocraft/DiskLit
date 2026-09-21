namespace DiskLit;

internal enum DataKind { WindowsCritical, Ntfs, Registry, DriverStore, WindowsUpdate, Store, Cache, GpuCache, LauncherCache, GameData, PersonalData, ApplicationData, Unknown }
internal enum DataRisk { Low, Review, High, Critical }

internal sealed record LocalText(string English, string Portuguese, string Spanish, string Russian)
{
    public override string ToString() => Strings.Current switch
    {
        Language.Portuguese => Portuguese, Language.Spanish => Spanish, Language.Russian => Russian, _ => English
    };
}

internal sealed record KnownDataRule(string Id, string Root, DataKind Kind, string Creator, bool Recreatable,
    DataRisk Risk, LocalText Description, LocalText Recommendation, bool OfferCleanup = false)
{
    public bool Matches(string path) => string.Equals(path, Root, StringComparison.OrdinalIgnoreCase) || Cleanup.IsInside(path, Root);
}

internal static class KnownDataCatalog
{
    static readonly LocalText CacheDescription = new("Rebuildable cache; the next launch may be slower or require downloads.", "Cache recriável; a próxima abertura pode ser mais lenta ou exigir downloads.", "Caché regenerable; el siguiente inicio puede ser más lento o requerir descargas.", "Восстанавливаемый кэш; следующий запуск может быть медленнее или потребовать загрузки.");
    static readonly LocalText CacheAdvice = new("Close the application and review the contents before recycling.", "Feche o aplicativo e revise o conteúdo antes de mover à Lixeira.", "Cierre la aplicación y revise el contenido antes de enviarlo a la Papelera.", "Закройте приложение и проверьте содержимое перед перемещением в корзину.");
    static readonly LocalText PreserveAdvice = new("Preserve. Use the application's own storage settings; back up personal data.", "Preserve. Use as opções de armazenamento do aplicativo e faça backup dos dados pessoais.", "Conserve. Use las opciones de almacenamiento de la aplicación y respalde los datos personales.", "Сохраните. Используйте настройки хранилища приложения и создайте резервную копию личных данных.");
    static readonly LocalText SystemAdvice = new("Preserve. Manage through Windows Settings, never by manual deletion.", "Preserve. Gerencie pelas Configurações do Windows, nunca por exclusão manual.", "Conserve. Administre desde Configuración de Windows, nunca mediante eliminación manual.", "Сохраните. Управляйте через параметры Windows, не удаляйте вручную.");
    static readonly LocalText PersonalDescription = new("Saves, mods, projects, screenshots or profile data; may be irreplaceable.", "Saves, mods, projetos, screenshots ou dados de perfil; podem ser insubstituíveis.", "Partidas, mods, proyectos, capturas o datos de perfil; pueden ser irremplazables.", "Сохранения, моды, проекты, снимки экрана или профиль; данные могут быть незаменимы.");
    static readonly LocalText AppDescription = new("Application data and settings. A product name does not make this disposable.", "Dados e configurações de aplicativo. O nome do produto não torna o conteúdo descartável.", "Datos y ajustes de aplicación. El nombre del producto no hace que sean desechables.", "Данные и настройки приложения. Название продукта не означает, что их можно удалить.");
    static readonly LocalText GameDescription = new("Installed game content; may include local modifications and personal data.", "Conteúdo de jogo instalado; pode incluir modificações locais e dados pessoais.", "Contenido de juego instalado; puede incluir modificaciones y datos personales.", "Содержимое установленной игры; может включать модификации и личные данные.");
    static readonly HashSet<string> PersonalNames = new(StringComparer.OrdinalIgnoreCase)
    { "saves", "save", "saved games", "savegames", "savedata", "mods", "mod", "screenshots", "screen shots", "captures", "projects", "documents", "worlds", "profiles", "profile", "userdata", "backups", ".git" };
    static readonly HashSet<string> PersonalExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".sav", ".save", ".ess", ".profile", ".world", ".replay", ".sln", ".csproj", ".blend", ".psd", ".docx", ".xlsx", ".pptx", ".kdbx" };

    public static IReadOnlyList<KnownDataRule> Rules { get; } = Build();

    public static KnownDataRule? Match(string fullPath)
    {
        if (!Path.IsPathFullyQualified(fullPath)) return null;
        var path = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar);
        var rule = Rules.FirstOrDefault(rule => rule.Matches(path));
        var markerPath = rule?.Recreatable == true ? Path.GetRelativePath(rule.Root, path) : path;
        if (HasPersonalMarker(markerPath) && rule?.Risk != DataRisk.Critical)
            return new("personal", path, DataKind.PersonalData, rule?.Creator ?? "—", false, DataRisk.High, PersonalDescription, PreserveAdvice);
        return rule;
    }

    static bool HasPersonalMarker(string path) => PersonalExtensions.Contains(Path.GetExtension(path))
        || path.Split(Path.DirectorySeparatorChar).Any(segment => PersonalNames.Contains(segment)
            || segment.Contains("save", StringComparison.OrdinalIgnoreCase)
            || segment.Contains("screenshot", StringComparison.OrdinalIgnoreCase)
            || segment.Contains("profile", StringComparison.OrdinalIgnoreCase)
            || segment.Contains("character", StringComparison.OrdinalIgnoreCase)
            || segment.Contains("project", StringComparison.OrdinalIgnoreCase)
            || segment.StartsWith("mods", StringComparison.OrdinalIgnoreCase));

    public static bool MustPreserve(string path)
    {
        var rule = Match(path);
        return rule is not null && !rule.Recreatable;
    }

    public static string Describe(string path, bool deep = false)
    {
        var rule = Match(path);
        var description = rule?.Description ?? new LocalText("Unclassified application data.", "Dados de aplicativo não classificados.", "Datos de aplicación sin clasificar.", "Неклассифицированные данные приложения.");
        var why = deep
            ? new LocalText("Old, large folder without a detected application match. This does not prove it is unused.", "Pasta antiga e grande sem correspondência detectada com aplicativo. Isso não prova desuso.", "Carpeta antigua y grande sin coincidencia detectada con aplicaciones. No demuestra desuso.", "Старая большая папка без найденного приложения. Это не доказывает, что она не используется.")
            : rule is null
                ? new LocalText("Inside a configured temporary location; inspect before selecting.", "Dentro de um local temporário configurado; revise antes de selecionar.", "En una ubicación temporal configurada; revise antes de seleccionar.", "В настроенной временной папке; проверьте перед выбором.")
                : new LocalText("Matches a known path rule", "Corresponde a uma regra de caminho conhecido", "Coincide con una regla de ruta conocida", "Совпадает с правилом известного пути");
        var risk = (rule?.Risk ?? DataRisk.Review) switch
        {
            DataRisk.Low => new LocalText("Low", "Baixo", "Bajo", "Низкий"),
            DataRisk.High => new LocalText("High", "Alto", "Alto", "Высокий"),
            DataRisk.Critical => new LocalText("Critical", "Crítico", "Crítico", "Критический"),
            _ => new LocalText("Review required", "Exige revisão", "Requiere revisión", "Требуется проверка")
        };
        var recreate = rule?.Recreatable == true ? new LocalText("Yes", "Sim", "Sí", "Да") : new LocalText("Not guaranteed", "Não garantido", "No garantizado", "Не гарантировано");
        return $"{description}\n{new LocalText("Likely creator", "Provável origem", "Origen probable", "Вероятный источник")}: {rule?.Creator ?? "—"} · {new LocalText("Recreatable", "Recriável", "Regenerable", "Восстанавливается")}: {recreate} · {new LocalText("Risk", "Risco", "Riesgo", "Риск")}: {risk}\n{why}{(rule is not null && !deep ? $": {rule.Root}" : "")}\n{rule?.Recommendation ?? PreserveAdvice}";
    }

    static List<KnownDataRule> Build()
    {
        var rules = new List<KnownDataRule>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var program = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var program86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var systemDescription = new LocalText("Windows-managed system data. Removing it can break startup, repair or updates.", "Dados gerenciados pelo Windows. A remoção pode prejudicar inicialização, reparo ou atualizações.", "Datos administrados por Windows. Quitarlos puede afectar al inicio, reparación o actualizaciones.", "Системные данные Windows. Удаление может нарушить запуск, восстановление или обновление.");
        void Add(string id, string root, DataKind kind, string creator, bool cache = false, bool offer = false)
        {
            var critical = kind is DataKind.WindowsCritical or DataKind.Ntfs or DataKind.Registry or DataKind.DriverStore or DataKind.WindowsUpdate or DataKind.Store;
            rules.Add(new(id, root.TrimEnd(Path.DirectorySeparatorChar), kind, creator, cache,
                critical ? DataRisk.Critical : cache ? DataRisk.Low : DataRisk.High,
                kind switch
                {
                    DataKind.Ntfs => new("Volume metadata, recovery or Recycle Bin storage.", "Metadados do volume, recuperação ou armazenamento da Lixeira.", "Metadatos del volumen, recuperación o almacenamiento de la Papelera.", "Метаданные тома, восстановление или хранилище корзины."),
                    DataKind.Registry => new("Registry hive: system or user settings.", "Arquivo do Registro: configurações do sistema ou usuário.", "Archivo del Registro: ajustes del sistema o usuario.", "Куст реестра: настройки системы или пользователя."),
                    DataKind.DriverStore => new("Driver packages used to install and repair hardware support.", "Pacotes de drivers usados para instalar e reparar o suporte ao hardware.", "Paquetes de controladores para instalar y reparar el soporte de hardware.", "Пакеты драйверов для установки и восстановления поддержки оборудования."),
                    DataKind.WindowsUpdate => new("Update downloads and servicing data, managed by Windows.", "Downloads de atualizações e dados de manutenção gerenciados pelo Windows.", "Descargas de actualizaciones y datos de mantenimiento de Windows.", "Загрузки обновлений и данные обслуживания Windows."),
                    DataKind.Store => new("Store applications and package data; may contain user settings and saves.", "Aplicativos da Store e dados de pacotes; podem conter configurações e saves.", "Aplicaciones de Store y datos de paquetes; pueden contener ajustes y partidas.", "Приложения Store и данные пакетов; могут содержать настройки и сохранения."),
                    _ => critical ? systemDescription : cache ? CacheDescription : kind == DataKind.PersonalData ? PersonalDescription : kind == DataKind.GameData ? GameDescription : AppDescription
                },
                critical ? SystemAdvice : cache ? CacheAdvice : PreserveAdvice, offer));
        }
        foreach (var name in new[] { "System32", "SysWOW64", "WinSxS", "Installer", "Boot" }) Add("windows-" + name, Path.Join(windows, name), DataKind.WindowsCritical, "Microsoft Windows");
        Add("driver-store", Path.Join(windows, "System32", "DriverStore"), DataKind.DriverStore, "Microsoft / hardware vendors");
        Add("registry-system", Path.Join(windows, "System32", "config"), DataKind.Registry, "Microsoft Windows");
        Add("registry-user", Path.Join(user, "NTUSER.DAT"), DataKind.Registry, "Microsoft Windows");
        Add("registry-classes", Path.Join(local, "Microsoft", "Windows", "UsrClass.dat"), DataKind.Registry, "Microsoft Windows");
        Add("windows-update", Path.Join(windows, "SoftwareDistribution"), DataKind.WindowsUpdate, "Microsoft Windows Update");
        Add("delivery-optimization", Path.Join(windows, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization"), DataKind.WindowsUpdate, "Microsoft Windows Update");
        Add("windows-apps", Path.Join(program, "WindowsApps"), DataKind.Store, "Microsoft Store");
        Add("store-user-data", Path.Join(local, "Packages"), DataKind.Store, "Microsoft Store apps");
        foreach (var drive in DriveInfo.GetDrives())
        {
            foreach (var name in new[] { "$MFT", "$MFTMirr", "$LogFile", "$Bitmap", "$Boot", "$Secure", "$UpCase", "$Extend", "$Volume", "System Volume Information", "$Recycle.Bin" })
                Add("ntfs-" + name, Path.Join(drive.Name, name), DataKind.Ntfs, "Microsoft Windows / NTFS");
            foreach (var name in new[] { "hiberfil.sys", "pagefile.sys", "swapfile.sys", "Windows.old" }) Add("system-" + name, Path.Join(drive.Name, name), DataKind.WindowsCritical, "Microsoft Windows");
        }
        foreach (var (vendor, folder) in new[] { ("NVIDIA", @"NVIDIA\DXCache"), ("NVIDIA", @"NVIDIA\GLCache"), ("NVIDIA", @"NVIDIA Corporation\NV_Cache"), ("AMD", @"AMD\DxCache"), ("AMD", @"AMD\GLCache"), ("AMD", @"AMD\VkCache"), ("Intel", @"Intel\ShaderCache"), ("Microsoft DirectX", "D3DSCache") })
            Add("gpu-" + folder, Path.Join(local, folder), DataKind.GpuCache, vendor, true, true);
        foreach (var (creator, root) in new[] { ("Google Chrome", Path.Join(local, @"Google\Chrome\User Data")), ("Microsoft Edge", Path.Join(local, @"Microsoft\Edge\User Data")), ("Opera", Path.Join(roaming, @"Opera Software\Opera Stable")), ("Opera GX", Path.Join(roaming, @"Opera Software\Opera GX Stable")), ("Discord", Path.Join(roaming, "discord")), ("Spotify", Path.Join(local, "Spotify")) })
        {
            Add("app-" + creator, root, DataKind.ApplicationData, creator);
            var profiles = creator is "Google Chrome" or "Microsoft Edge" ? ProfileNames(root).Prepend("Default") : new[] { "" };
            foreach (var profile in profiles.Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache" })
                Add("cache-" + creator + "-" + profile + "-" + cache, Path.Join(root, profile, cache), DataKind.Cache, creator, true, true);
        }
        Add("spotify-storage", Path.Join(local, @"Spotify\Storage"), DataKind.ApplicationData, "Spotify");
        Add("spotify-profile", Path.Join(roaming, "Spotify"), DataKind.ApplicationData, "Spotify");
        var firefox = Path.Join(local, @"Mozilla\Firefox\Profiles");
        foreach (var profile in ProfileNames(firefox, "*"))
            Add("firefox-cache-" + profile, Path.Join(firefox, profile, "cache2"), DataKind.Cache, "Mozilla Firefox", true, true);
        Add("firefox-profiles", Path.Join(roaming, @"Mozilla\Firefox"), DataKind.ApplicationData, "Mozilla Firefox");
        foreach (var edition in new[] { "Opera Stable", "Opera GX Stable" })
        {
            var opera = Path.Join(local, "Opera Software", edition);
            Add("opera-local-" + edition, opera, DataKind.ApplicationData, "Opera");
            foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache" })
                Add("opera-local-cache-" + edition + cache, Path.Join(opera, cache), DataKind.Cache, "Opera", true, true);
        }
        foreach (var (creator, root, cache) in new[] { ("Steam", Path.Join(program86, "Steam"), "appcache"), ("Epic Games", Path.Join(local, @"EpicGamesLauncher\Saved"), "webcache"), ("Battle.net", Path.Join(local, "Battle.net"), "Cache"), ("Riot Games", Path.Join(local, "Riot Games"), "") })
        {
            Add("launcher-" + creator, root, DataKind.ApplicationData, creator);
            if (cache.Length > 0) Add("launcher-cache-" + creator, Path.Join(root, cache), DataKind.LauncherCache, creator, true, true);
        }
        Add("steam-games", Path.Join(program86, @"Steam\steamapps"), DataKind.GameData, "Steam / game developers");
        Add("steam-userdata", Path.Join(program86, @"Steam\userdata"), DataKind.PersonalData, "Steam");
        Add("epic-games", Path.Join(program, "Epic Games"), DataKind.GameData, "Epic Games / game developers");
        foreach (var (creator, root) in new[] { ("Minecraft", Path.Join(roaming, ".minecraft")), ("Electronic Arts / The Sims", Path.Join(user, @"Documents\Electronic Arts")), ("Rockstar Games", Path.Join(user, @"Documents\Rockstar Games")), ("Games", Path.Join(user, @"Documents\My Games")), ("Games", Path.Join(user, "Saved Games")) })
            Add("game-personal-" + creator, root, DataKind.PersonalData, creator);
        return rules.OrderByDescending(rule => rule.Root.Length).ToList();
    }

    static string[] ProfileNames(string root, string pattern = "Profile *")
    {
        try
        {
            if (!Directory.Exists(root) || !CleanupInspection.SafePath(root)) return [];
            return Directory.GetDirectories(root, pattern).Select(Path.GetFileName).OfType<string>().ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }
}
