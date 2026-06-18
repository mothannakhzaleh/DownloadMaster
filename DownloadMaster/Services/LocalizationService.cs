using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed class LocalizationService
{
    private static readonly Dictionary<string, string> ExtendedEnglish = new()
    {
        ["TabVideo"] = "Video",
        ["TabInstagram"] = "Instagram",
        ["InstagramHint"] = "Paste a post/reel/story link to download directly, or paste a profile (@username) and click Fetch to browse stories, highlights, and posts.\n\nCookies: click Sync from browser (close Edge/Chrome first) or Add cookies to paste from DevTools.",
        ["InstagramProfileUrlError"] = "Could not load that profile. Make sure cookies are fresh and the username is correct.",
        ["InstagramInvalidProfile"] = "That does not look like a valid Instagram profile link.",
        ["InstagramProfileEmpty"] = "No stories, highlights, or posts were found for that profile with your current session.",
        ["InstagramProfileLoaded"] = "Loaded {0} items from @{1}. Select items below or download a whole section.",
        ["InstagramStoriesBatch"] = "All current stories",
        ["InstagramRateLimited"] = "Instagram blocked this request temporarily (HTTP 429).\n\nWait 10–15 minutes, avoid VPN if possible, then try Fetch once.\n\nDirect post/reel links still work with Download.",
        ["InstagramProfileApiBlocked"] = "Instagram blocked the profile API, but the profile page was loaded.\n\nTry Fetch again in a few minutes, or paste direct post/reel links and click Download.",
        ["InstagramProfilePartial"] = "Posts/highlights may be incomplete because Instagram limited the profile API. Stories and items found on the page are still listed below.",
        ["InstagramPhotoPostHint"] = "This post looks like a photo. Update yt-dlp (run setup-tools.bat), then click Download to save the image.",
        ["InstagramSyncCookies"] = "Sync from browser",
        ["InstagramSyncCookiesHint"] = "Log in to Instagram in your browser, close the browser completely, then click Sync from browser.",
        ["InstagramSyncCloseChrome"] = "Close Google Chrome completely (check the system tray), then try Sync from browser again.",
        ["InstagramSyncCloseEdge"] = "Close Microsoft Edge completely (check the system tray), then try Sync from browser again.",
        ["InstagramSyncNoSession"] = "Browser cookies were exported, but no Instagram login was found. Sign in to instagram.com in your browser first.",
        ["InstagramSyncNoDatabase"] = "Could not find a Chrome/Edge cookie database on this PC. Use Add cookies... and paste from DevTools instead.",
        ["InstagramDownloadSelected"] = "Download selected",
        ["InstagramSelectAll"] = "Select all",
        ["InstagramDeselectAll"] = "Deselect all",
        ["InstagramDownloadSection"] = "Download section",
        ["InstagramDownloadOne"] = "Download",
        ["InstagramExpandAll"] = "Expand all",
        ["InstagramCollapseAll"] = "Collapse all",
        ["InstagramSectionHeader"] = "{0} ({1})",
        ["DownloadQueue"] = "Downloads",
        ["DownloadQueueCount"] = "Downloads ({0})",
        ["InstagramSectionStories"] = "Stories",
        ["InstagramSectionPosts"] = "Posts",
        ["InstagramSectionHighlight"] = "Highlight",
        ["InstagramHighlightSection"] = "Highlight: {0}",
        ["InstagramHighlightBatch"] = "All items in highlight",
        ["InstagramHighlightStoryItem"] = "{0}/{1} · {2}",
        ["InstagramStoryItem"] = "Story · {0}",
        ["InstagramStoryItemGeneric"] = "Story",
        ["InstagramPostItem"] = "{0} · {1}",
        ["InstagramCarouselBatch"] = "{0} · All images ({1})",
        ["InstagramCarouselSlideItem"] = "{0} · Image {1}/{2} · {3}",
        ["InstagramKindVideo"] = "Video",
        ["InstagramKindPhoto"] = "Photo",
        ["InstagramExtractError"] = "yt-dlp could not read that Instagram page. Use a direct reel/post/story link, make sure cookies are fresh, then try again.",
        ["InstagramUrlPlaceholder"] = "Paste reel, story, post, or @username link...",
        ["InstagramBrowser"] = "Browser for login",
        ["BrowserChrome"] = "Google Chrome",
        ["BrowserEdge"] = "Microsoft Edge",
        ["InstagramLogin"] = "Open Instagram login",
        ["InstagramAddCookies"] = "Add cookies...",
        ["InstagramImportFromFile"] = "Import from file...",
        ["InstagramSaveCookies"] = "Save and use",
        ["InstagramPasteHint"] = "In DevTools (F12 → Network), click an instagram.com request.\nUnder Request Headers, right-click the Cookie row and choose \"Copy value\" (do not highlight — that truncates long cookies).\n\nPaste below. DownloadMaster builds cookies.txt automatically.",
        ["InstagramClearCookies"] = "Clear cookies",
        ["InstagramConfirmClearCookies"] = "Remove saved Instagram cookies from this PC?\n\nYou will need to sync or add cookies again before profile fetch and private downloads work.",
        ["InstagramCookiesReady"] = "Cookies ready",
        ["InstagramCookiesMissing"] = "No cookies added yet",
        ["InstagramCookiesInvalid"] = "Could not find a valid Instagram session in that text. Copy the Cookie header while logged in to instagram.com.",
        ["InstagramCookiesFileError"] = "The saved cookies file could not be read by yt-dlp. Click Add cookies again and paste a fresh Cookie header.",
        ["About"] = "About",
        ["AboutVersion"] = "Version",
        ["AboutAuthorPrefix"] = "Copyright © 2026 ",
        ["AboutDescription"] = "Native Windows download manager for videos and Instagram. Powered by yt-dlp and FFmpeg.",
        ["AboutFeatures"] = "• 1000+ sites (YouTube, X, and more) with fetch preview and download queue\n• Instagram profile browse — stories, highlights, posts, and carousels with thumbnails\n• Per-item or batch selection; direct photo/reel downloads via Instagram API\n• Cookies: sync from Chrome/Edge, paste from DevTools, or clear\n• Play / open folder on complete · failure diagnostics · dark/light · English & Arabic",
        ["AboutLicense"] = "MIT License. Third-party tools have their own licenses. See THIRD_PARTY_LICENSES.md.",
        ["AboutClose"] = "Close",
        ["InstagramAuthError"] = "Instagram cookies are missing or expired.\n\n1. Open Instagram login and sign in.\n2. Copy the Cookie header from DevTools → Network.\n3. Click Add cookies, paste it, and Save and use.\n4. Try Fetch or Download again.\n\nPrivate posts also require that your account follows that user.",
        ["LinkPlaceholder"] = "Put your link here...",
        ["Remove"] = "Remove",
        ["CopyDetails"] = "Copy details",
        ["CopyDetailsCopied"] = "Download details copied to clipboard. Paste them here so we can diagnose the issue.",
        ["ClearList"] = "Clear list",
        ["ConfirmClearList"] = "Remove all items from the download list?",
    };

    private static readonly Dictionary<string, string> ExtendedArabic = new()
    {
        ["TabVideo"] = "فيديو",
        ["TabInstagram"] = "إنستغرام",
        ["InstagramHint"] = "الصق رابط post/reel/story للتنزيل مباشرة، أو الصق ملفاً (@username) واضغط جلب لتصفح stories و highlights و posts.\n\nCookies: اضغط مزامنة من المتصفح (أغلق Edge/Chrome أولاً) أو إضافة cookies للصق من DevTools.",
        ["InstagramProfileUrlError"] = "تعذر تحميل هذا الملف. تأكد أن cookies حديثة واسم المستخدم صحيح.",
        ["InstagramInvalidProfile"] = "هذا لا يبدو رابط ملف إنستغرام صالح.",
        ["InstagramProfileEmpty"] = "لم يُعثر على stories أو highlights أو posts لهذا الملف بجلستك الحالية.",
        ["InstagramProfileLoaded"] = "تم تحميل {0} عنصر من @{1}. اختر العناصر أدناه أو نزّل قسمًا كاملًا.",
        ["InstagramStoriesBatch"] = "كل stories الحالية",
        ["InstagramRateLimited"] = "حظر إنستغرام هذا الطلب مؤقتاً (HTTP 429).\n\nانتظر 10–15 دقيقة، وتجنب VPN إن أمكن، ثم حاول جلب مرة واحدة.\n\nروابط post/reel المباشرة ما زالت تعمل مع تنزيل.",
        ["InstagramProfileApiBlocked"] = "حظر إنستغرام واجهة الملف، لكن صفحة الملف تم تحميلها.\n\nحاول الجلب بعد دقائق، أو الصق روابط post/reel مباشرة واضغط تنزيل.",
        ["InstagramProfilePartial"] = "قد تكون posts/highlights غير مكتملة لأن إنستغرام حدّ واجهة الملف. stories والعناصر الموجودة في الصفحة ما زالت معروضة أدناه.",
        ["InstagramPhotoPostHint"] = "يبدو أن هذا منشور صورة. حدّث yt-dlp (شغّل setup-tools.bat) ثم اضغط تنزيل لحفظ الصورة.",
        ["InstagramSyncCookies"] = "مزامنة من المتصفح",
        ["InstagramSyncCookiesHint"] = "سجّل الدخول إلى إنستغرام في المتصفح، أغلق المتصفح بالكامل، ثم اضغط مزامنة من المتصفح.",
        ["InstagramSyncCloseChrome"] = "أغلق Google Chrome بالكامل (تحقق من أيقونة system tray)، ثم حاول مزامنة من المتصفح مجدداً.",
        ["InstagramSyncCloseEdge"] = "أغلق Microsoft Edge بالكامل (تحقق من أيقونة system tray)، ثم حاول مزامنة من المتصفح مجدداً.",
        ["InstagramSyncNoSession"] = "تم تصدير cookies المتصفح، لكن لم يُعثر على تسجيل دخول إنستغرام. سجّل الدخول إلى instagram.com في المتصفح أولاً.",
        ["InstagramSyncNoDatabase"] = "تعذر العثور على قاعدة cookies لـ Chrome/Edge على هذا الجهاز. استخدم إضافة cookies... والصق من DevTools.",
        ["InstagramDownloadSelected"] = "تنزيل المحدد",
        ["InstagramSelectAll"] = "تحديد الكل",
        ["InstagramDeselectAll"] = "إلغاء تحديد الكل",
        ["InstagramDownloadSection"] = "تنزيل القسم",
        ["InstagramDownloadOne"] = "تنزيل",
        ["InstagramExpandAll"] = "توسيع الكل",
        ["InstagramCollapseAll"] = "طي الكل",
        ["InstagramSectionHeader"] = "{0} ({1})",
        ["DownloadQueue"] = "التنزيلات",
        ["DownloadQueueCount"] = "التنزيلات ({0})",
        ["InstagramSectionStories"] = "Stories",
        ["InstagramSectionPosts"] = "Posts",
        ["InstagramSectionHighlight"] = "Highlight",
        ["InstagramHighlightSection"] = "Highlight: {0}",
        ["InstagramHighlightBatch"] = "كل عناصر الـ highlight",
        ["InstagramHighlightStoryItem"] = "{0}/{1} · {2}",
        ["InstagramStoryItem"] = "Story · {0}",
        ["InstagramStoryItemGeneric"] = "Story",
        ["InstagramPostItem"] = "{0} · {1}",
        ["InstagramCarouselBatch"] = "{0} · كل الصور ({1})",
        ["InstagramCarouselSlideItem"] = "{0} · صورة {1}/{2} · {3}",
        ["InstagramKindVideo"] = "فيديو",
        ["InstagramKindPhoto"] = "صورة",
        ["InstagramExtractError"] = "تعذر على yt-dlp قراءة صفحة إنستغرام هذه. استخدم رابط reel/post/story مباشر وتأكد أن cookies حديثة.",
        ["InstagramUrlPlaceholder"] = "الصق رابط reel أو story أو منشور أو @username...",
        ["InstagramBrowser"] = "المتصفح لتسجيل الدخول",
        ["BrowserChrome"] = "Google Chrome",
        ["BrowserEdge"] = "Microsoft Edge",
        ["InstagramLogin"] = "فتح تسجيل دخول إنستغرام",
        ["InstagramAddCookies"] = "إضافة cookies...",
        ["InstagramImportFromFile"] = "استيراد من ملف...",
        ["InstagramSaveCookies"] = "حفظ واستخدام",
        ["InstagramPasteHint"] = "في DevTools (F12 → Network)، اختر طلب instagram.com.\nضمن Request Headers، انقر يميناً على Cookie واختر \"Copy value\" (لا تحدد النص يدوياً لأنه يُقطع).\n\nالصق أدناه. سيبني داونلود ماستر cookies.txt تلقائياً.",
        ["InstagramClearCookies"] = "مسح cookies",
        ["InstagramConfirmClearCookies"] = "إزالة cookies إنستغرام المحفوظة من هذا الجهاز؟\n\nستحتاج إلى مزامنة أو إضافة cookies مرة أخرى قبل جلب الملفات والتنزيلات الخاصة.",
        ["InstagramCookiesReady"] = "cookies جاهزة",
        ["InstagramCookiesMissing"] = "لم تتم إضافة cookies بعد",
        ["InstagramCookiesInvalid"] = "تعذر العثور على جلسة إنستغرام صالحة. انسخ رأس Cookie وأنت مسجّل الدخول.",
        ["InstagramCookiesFileError"] = "تعذر على yt-dlp قراءة ملف cookies المحفوظ. اضغط إضافة cookies والصق رأس Cookie جديداً.",
        ["About"] = "حول",
        ["AboutVersion"] = "الإصدار",
        ["AboutAuthorPrefix"] = "حقوق النشر © 2026 ",
        ["AboutDescription"] = "مدير تنزيل فيديو وإنستغرام لنظام Windows. يعمل عبر yt-dlp و FFmpeg.",
        ["AboutFeatures"] = "• أكثر من 1000 موقع (YouTube و X وغيرها) مع معاينة وقائمة تنزيل\n• تصفح ملف إنستغرام — stories و highlights ومنشورات وcarousels مع صور مصغرة\n• اختيار عنصر أو مجموعة؛ تنزيل صور/reels مباشرة عبر Instagram API\n• cookies: مزامنة من Chrome/Edge أو لصق من DevTools أو مسح\n• تشغيل / فتح المجلد · تشخيص الأخطاء · ثيم فاتح/داكن · عربي وإنجليزي",
        ["AboutLicense"] = "رخصة MIT. للأدوات الخارجية رخصها الخاصة. راجع THIRD_PARTY_LICENSES.md.",
        ["AboutClose"] = "إغلاق",
        ["InstagramAuthError"] = "ملف cookies لإنستغرام مفقود أو منتهي.\n\n1. افتح تسجيل دخول إنستغرام وسجّل الدخول.\n2. انسخ رأس Cookie من DevTools → Network.\n3. اضغط إضافة cookies، الصقه، ثم حفظ واستخدام.\n4. حاول جلب أو تنزيل مرة أخرى.\n\nالمنشورات الخاصة تتطلب أيضاً متابعة ذلك الحساب.",
        ["LinkPlaceholder"] = "ضع الرابط هنا...",
        ["Remove"] = "إزالة",
        ["CopyDetails"] = "نسخ التفاصيل",
        ["CopyDetailsCopied"] = "تم نسخ تفاصيل التنزيل. الصقها هنا لتشخيص المشكلة.",
        ["ClearList"] = "مسح القائمة",
        ["ConfirmClearList"] = "إزالة كل العناصر من قائمة التنزيل؟",
    };

    private static readonly Dictionary<AppLanguage, Dictionary<string, string>> Strings = CreateStrings();

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

    private static Dictionary<AppLanguage, Dictionary<string, string>> CreateStrings()
    {
        var map = BuildStrings();
        MergeExtended(map);
        return map;
    }

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

    private static void MergeExtended(Dictionary<AppLanguage, Dictionary<string, string>> map)
    {
        ApplyExtended(map[AppLanguage.English], ExtendedEnglish);
        ApplyExtended(map[AppLanguage.Arabic], ExtendedArabic);
        foreach (var lang in map.Keys.Where(k => k is not AppLanguage.English and not AppLanguage.Arabic))
            ApplyExtended(map[lang], ExtendedEnglish);
    }

    private static void ApplyExtended(Dictionary<string, string> target, Dictionary<string, string> extra)
    {
        foreach (var (key, value) in extra)
            target[key] = value;
    }
}
