using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed class LocalizationService
{
    private static readonly Dictionary<AppLanguage, Dictionary<string, string>> Strings = BuildStrings();

    public AppLanguage Current { get; private set; } = AppLanguage.English;

    public string Get(string key) =>
        Strings.TryGetValue(Current, out var lang) && lang.TryGetValue(key, out var value)
            ? value
            : Strings[AppLanguage.English].GetValueOrDefault(key, key);

    public void SetLanguage(AppLanguage language) => Current = language;

    public bool IsRtl => Current == AppLanguage.Arabic;

    public static string GetNativeLanguageName(AppLanguage language) => language switch
    {
        AppLanguage.English => "English",
        AppLanguage.Arabic => "العربية",
        AppLanguage.Spanish => "Español",
        AppLanguage.French => "Français",
        AppLanguage.German => "Deutsch",
        AppLanguage.Chinese => "中文",
        AppLanguage.Swedish => "Svenska",
        AppLanguage.Norwegian => "Norsk",
        _ => language.ToString()
    };

    public static IReadOnlyList<AppLanguage> SupportedLanguages { get; } =
        Enum.GetValues<AppLanguage>().Cast<AppLanguage>().ToList();

    private static Dictionary<AppLanguage, Dictionary<string, string>> BuildStrings() => new()
    {
        [AppLanguage.English] = En(
            "DownloadMaster", "Multi-platform video download manager",
            "Paste YouTube, X/Twitter, or direct URL...", "Fetch", "Download",
            "Quality", "Format", "Settings", "No downloads yet. Paste a URL to get started.",
            "Save", "Cancel", "Reset defaults", "General", "Network", "Appearance", "Language",
            "Default save folder", "Max concurrent downloads", "Theme", "Dark", "Light",
            "Ready", "yt-dlp not found", "FFmpeg not found",
            "Done", "Failed", "Cancelled", "Queued", "Fetching info...", "Downloading...", "Fetching...",
            "Open folder", "Play"),

        [AppLanguage.Arabic] = Ar(
            "داونلود ماستر", "مدير تنزيل الفيديو",
            "الصق رابط يوتيوب أو X أو رابط مباشر...", "جلب", "تنزيل",
            "الجودة", "الصيغة", "الإعدادات", "لا توجد تنزيلات بعد.",
            "حفظ", "إلغاء", "إعادة الافتراضي", "عام", "الشبكة", "المظهر", "اللغة",
            "مجلد الحفظ", "التنزيلات المتزامنة", "المظهر", "داكن", "فاتح",
            "جاهز", "yt-dlp غير موجود", "FFmpeg غير موجود",
            "اكتمل", "فشل", "أُلغي", "في الانتظار", "جاري جلب المعلومات...", "جاري التنزيل...", "جاري الجلب...",
            "فتح المجلد", "تشغيل"),

        [AppLanguage.Spanish] = L(
            "DownloadMaster", "Gestor de descargas de video multiplataforma",
            "Pega un enlace de YouTube, X/Twitter o URL directa...", "Obtener", "Descargar",
            "Calidad", "Formato", "Ajustes", "Aún no hay descargas. Pega un enlace para empezar.",
            "Guardar", "Cancelar", "Restablecer", "General", "Red", "Apariencia", "Idioma",
            "Carpeta de guardado", "Descargas simultáneas", "Tema", "Oscuro", "Claro",
            "Listo", "yt-dlp no encontrado", "FFmpeg no encontrado",
            "Completado", "Fallido", "Cancelado", "En cola", "Obteniendo información...", "Descargando...", "Obteniendo...",
            "Abrir carpeta", "Reproducir"),

        [AppLanguage.French] = L(
            "DownloadMaster", "Gestionnaire de téléchargement vidéo multiplateforme",
            "Collez un lien YouTube, X/Twitter ou une URL directe...", "Analyser", "Télécharger",
            "Qualité", "Format", "Paramètres", "Aucun téléchargement. Collez une URL pour commencer.",
            "Enregistrer", "Annuler", "Réinitialiser", "Général", "Réseau", "Apparence", "Langue",
            "Dossier de téléchargement", "Téléchargements simultanés", "Thème", "Sombre", "Clair",
            "Prêt", "yt-dlp introuvable", "FFmpeg introuvable",
            "Terminé", "Échec", "Annulé", "En attente", "Récupération des infos...", "Téléchargement...", "Analyse...",
            "Ouvrir le dossier", "Lire"),

        [AppLanguage.German] = L(
            "DownloadMaster", "Multiplattform-Video-Download-Manager",
            "YouTube-, X/Twitter- oder direkte URL einfügen...", "Abrufen", "Herunterladen",
            "Qualität", "Format", "Einstellungen", "Noch keine Downloads. URL einfügen zum Starten.",
            "Speichern", "Abbrechen", "Zurücksetzen", "Allgemein", "Netzwerk", "Darstellung", "Sprache",
            "Speicherordner", "Gleichzeitige Downloads", "Design", "Dunkel", "Hell",
            "Bereit", "yt-dlp nicht gefunden", "FFmpeg nicht gefunden",
            "Fertig", "Fehlgeschlagen", "Abgebrochen", "Wartend", "Infos werden abgerufen...", "Wird heruntergeladen...", "Wird abgerufen...",
            "Ordner öffnen", "Abspielen"),

        [AppLanguage.Chinese] = L(
            "DownloadMaster", "多平台视频下载管理器",
            "粘贴 YouTube、X/Twitter 或直接链接...", "获取", "下载",
            "画质", "格式", "设置", "暂无下载。粘贴链接开始使用。",
            "保存", "取消", "恢复默认", "常规", "网络", "外观", "语言",
            "默认保存文件夹", "最大并发下载数", "主题", "深色", "浅色",
            "就绪", "未找到 yt-dlp", "未找到 FFmpeg",
            "完成", "失败", "已取消", "排队中", "正在获取信息...", "正在下载...", "正在获取...",
            "打开文件夹", "播放"),

        [AppLanguage.Swedish] = L(
            "DownloadMaster", "Video-nedladdningshanterare för flera plattformar",
            "Klistra in YouTube-, X/Twitter- eller direktlänk...", "Hämta", "Ladda ner",
            "Kvalitet", "Format", "Inställningar", "Inga nedladdningar ännu. Klistra in en länk för att börja.",
            "Spara", "Avbryt", "Återställ standard", "Allmänt", "Nätverk", "Utseende", "Språk",
            "Standardmapp", "Samtidiga nedladdningar", "Tema", "Mörkt", "Ljust",
            "Klar", "yt-dlp hittades inte", "FFmpeg hittades inte",
            "Klar", "Misslyckades", "Avbruten", "I kö", "Hämtar info...", "Laddar ner...", "Hämtar...",
            "Öppna mapp", "Spela upp"),

        [AppLanguage.Norwegian] = L(
            "DownloadMaster", "Video-nedlastingsbehandler for flere plattformer",
            "Lim inn YouTube-, X/Twitter- eller direktelenke...", "Hent", "Last ned",
            "Kvalitet", "Format", "Innstillinger", "Ingen nedlastinger ennå. Lim inn en lenke for å starte.",
            "Lagre", "Avbryt", "Tilbakestill standard", "Generelt", "Nettverk", "Utseende", "Språk",
            "Standardmappe", "Samtidige nedlastinger", "Tema", "Mørk", "Lys",
            "Klar", "yt-dlp ikke funnet", "FFmpeg ikke funnet",
            "Ferdig", "Mislyktes", "Avbrutt", "I kø", "Henter info...", "Laster ned...", "Henter...",
            "Åpne mappe", "Spill av"),
    };

    private static Dictionary<string, string> En(
        string appTitle, string appSubtitle, string urlPlaceholder, string fetch, string download,
        string quality, string format, string settings, string noDownloads,
        string save, string cancel, string reset, string general, string network, string appearance, string language,
        string savePath, string maxConcurrent, string theme, string themeDark, string themeLight,
        string toolsReady, string toolsMissingYtDlp, string toolsMissingFfmpeg,
        string statusDone, string statusFailed, string statusCancelled, string statusQueued, string statusFetching, string statusDownloading, string fetching,
        string openFolder, string play) => Dict(
        appTitle, appSubtitle, urlPlaceholder, fetch, download, quality, format, settings, noDownloads,
        save, cancel, reset, general, network, appearance, language, savePath, maxConcurrent, theme, themeDark, themeLight,
        toolsReady, toolsMissingYtDlp, toolsMissingFfmpeg, statusDone, statusFailed, statusCancelled, statusQueued, statusFetching, statusDownloading, fetching,
        openFolder, play);

    private static Dictionary<string, string> Ar(
        string appTitle, string appSubtitle, string urlPlaceholder, string fetch, string download,
        string quality, string format, string settings, string noDownloads,
        string save, string cancel, string reset, string general, string network, string appearance, string language,
        string savePath, string maxConcurrent, string theme, string themeDark, string themeLight,
        string toolsReady, string toolsMissingYtDlp, string toolsMissingFfmpeg,
        string statusDone, string statusFailed, string statusCancelled, string statusQueued, string statusFetching, string statusDownloading, string fetching,
        string openFolder, string play) => Dict(
        appTitle, appSubtitle, urlPlaceholder, fetch, download, quality, format, settings, noDownloads,
        save, cancel, reset, general, network, appearance, language, savePath, maxConcurrent, theme, themeDark, themeLight,
        toolsReady, toolsMissingYtDlp, toolsMissingFfmpeg, statusDone, statusFailed, statusCancelled, statusQueued, statusFetching, statusDownloading, fetching,
        openFolder, play);

    private static Dictionary<string, string> L(
        string appTitle, string appSubtitle, string urlPlaceholder, string fetch, string download,
        string quality, string format, string settings, string noDownloads,
        string save, string cancel, string reset, string general, string network, string appearance, string language,
        string savePath, string maxConcurrent, string theme, string themeDark, string themeLight,
        string toolsReady, string toolsMissingYtDlp, string toolsMissingFfmpeg,
        string statusDone, string statusFailed, string statusCancelled, string statusQueued, string statusFetching, string statusDownloading, string fetching,
        string openFolder, string play) => Dict(
        appTitle, appSubtitle, urlPlaceholder, fetch, download, quality, format, settings, noDownloads,
        save, cancel, reset, general, network, appearance, language, savePath, maxConcurrent, theme, themeDark, themeLight,
        toolsReady, toolsMissingYtDlp, toolsMissingFfmpeg, statusDone, statusFailed, statusCancelled, statusQueued, statusFetching, statusDownloading, fetching,
        openFolder, play);

    private static Dictionary<string, string> Dict(
        string appTitle, string appSubtitle, string urlPlaceholder, string fetch, string download,
        string quality, string format, string settings, string noDownloads,
        string save, string cancel, string reset, string general, string network, string appearance, string language,
        string savePath, string maxConcurrent, string theme, string themeDark, string themeLight,
        string toolsReady, string toolsMissingYtDlp, string toolsMissingFfmpeg,
        string statusDone, string statusFailed, string statusCancelled, string statusQueued, string statusFetching, string statusDownloading, string fetching,
        string openFolder, string play) => new()
    {
        ["AppTitle"] = appTitle,
        ["AppSubtitle"] = appSubtitle,
        ["UrlPlaceholder"] = urlPlaceholder,
        ["Fetch"] = fetch,
        ["Download"] = download,
        ["Quality"] = quality,
        ["Format"] = format,
        ["Settings"] = settings,
        ["NoDownloads"] = noDownloads,
        ["Save"] = save,
        ["Cancel"] = cancel,
        ["Reset"] = reset,
        ["General"] = general,
        ["Network"] = network,
        ["Appearance"] = appearance,
        ["Language"] = language,
        ["SavePath"] = savePath,
        ["MaxConcurrent"] = maxConcurrent,
        ["Theme"] = theme,
        ["ThemeDark"] = themeDark,
        ["ThemeLight"] = themeLight,
        ["ToolsReady"] = toolsReady,
        ["ToolsMissingYtDlp"] = toolsMissingYtDlp,
        ["ToolsMissingFfmpeg"] = toolsMissingFfmpeg,
        ["StatusDone"] = statusDone,
        ["StatusFailed"] = statusFailed,
        ["StatusCancelled"] = statusCancelled,
        ["StatusQueued"] = statusQueued,
        ["StatusFetching"] = statusFetching,
        ["StatusDownloading"] = statusDownloading,
        ["Fetching"] = fetching,
        ["OpenFolder"] = openFolder,
        ["Play"] = play,
    };
}
