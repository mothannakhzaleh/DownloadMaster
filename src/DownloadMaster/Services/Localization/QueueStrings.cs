namespace DownloadMaster.Services.Localization;

/// <summary>Strings for queues, scheduler, categories, batch downloads, clipboard monitor and autostart. Keys here override the legacy tables (see LocalizationService.Get).</summary>
public static class QueueStrings
{
    public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        // Settings
        ["SettingsSortCategories"] = "Sort downloads into category folders (Video, Music, Programs, Compressed, Documents, Images, Other)",
        ["SettingsClipboardMonitor"] = "Watch the clipboard for download links",
        ["SettingsStartWithOs"] = "Start DownloadMaster when I sign in (in the tray)",
        ["SettingsStartWithOsFailed"] = "Could not change the sign-in setting: {0}",
        ["SettingsSchedulerHeader"] = "Queue scheduler",
        ["SettingsSchedulerStart"] = "Start downloads at",
        ["SettingsSchedulerStop"] = "Pause downloads at",
        ["SettingsSchedulerHint"] = "Daily, local time (24-hour HH:mm). Outside the window new downloads wait; at the stop time running file downloads pause and resume in the next window.",
        ["SettingsSchedulerInvalidTime"] = "Enter the time as HH:mm (for example 02:00).",
        ["SettingsBrowserExtension"] = "Browser extension…",

        // Queue header
        ["QueueWhenDone"] = "When all downloads finish:",
        ["FinishNothing"] = "Do nothing",
        ["FinishSleep"] = "Sleep",
        ["FinishShutDown"] = "Shut down",
        ["FinishQuitApp"] = "Quit DownloadMaster",
        ["QueueScheduledFor"] = "Scheduled for {0}",
        ["QueueScheduleWindow"] = "Scheduler: {0}",
        ["QueueScheduleHeld"] = "{0} waiting for {1}",
        ["QueueStartNow"] = "Start now",

        // Finish countdown
        ["FinishCountdownTitle"] = "All downloads finished",
        ["FinishCountdownSleep"] = "The computer will sleep in {0} seconds.",
        ["FinishCountdownShutDown"] = "The computer will shut down in {0} seconds.",
        ["FinishCountdownQuitApp"] = "DownloadMaster will quit in {0} seconds.",
        ["FinishCountdownNow"] = "Do it now",
        ["FinishCountdownCancel"] = "Cancel",
        ["FinishActionFailed"] = "Could not run the finish action: {0}",

        // Batch downloader
        ["BatchButton"] = "Batch download…",
        ["BatchTitle"] = "Batch download",
        ["BatchHint"] = "Use [from-to] ranges: part_[01-20].rar (zero padding is kept), img_[a-f].jpg (letters), shot_[0-100:5].png (step 5). Several ranges multiply.",
        ["BatchPatternPlaceholder"] = "https://example.com/part_[01-20].rar",
        ["BatchPreview"] = "Preview",
        ["BatchCount"] = "{0} links",
        ["BatchMore"] = "… and {0} more",
        ["BatchNoPattern"] = "Add a range like [1-10] or [a-f] to the link.",
        ["BatchInvalid"] = "Invalid pattern: {0}",
        ["BatchInvalidUrl"] = "The expanded links must start with http:// or https://.",
        ["BatchQueueAll"] = "Download all",
        ["BatchQueued"] = "{0} downloads added to the queue.",

        // Copy SHA-256
        ["CopySha256"] = "Copy SHA-256",
        ["CopySha256Copied"] = "SHA-256 copied to the clipboard.",
    };

    public static readonly IReadOnlyDictionary<string, string> Arabic = new Dictionary<string, string>
    {
        ["SettingsSortCategories"] = "فرز التنزيلات في مجلدات حسب النوع (فيديو، موسيقى، برامج، مضغوطة، مستندات، صور، أخرى)",
        ["SettingsClipboardMonitor"] = "مراقبة الحافظة لالتقاط روابط التنزيل",
        ["SettingsStartWithOs"] = "تشغيل DownloadMaster عند تسجيل الدخول (في شريط النظام)",
        ["SettingsStartWithOsFailed"] = "تعذر تغيير إعداد التشغيل عند تسجيل الدخول: {0}",
        ["SettingsSchedulerHeader"] = "جدولة قائمة التنزيل",
        ["SettingsSchedulerStart"] = "بدء التنزيلات عند",
        ["SettingsSchedulerStop"] = "إيقاف التنزيلات مؤقتاً عند",
        ["SettingsSchedulerHint"] = "يومياً بالتوقيت المحلي (24 ساعة HH:mm). خارج الفترة تنتظر التنزيلات الجديدة؛ وعند وقت الإيقاف تتوقف تنزيلات الملفات الجارية مؤقتاً وتُستأنف في الفترة التالية.",
        ["SettingsSchedulerInvalidTime"] = "أدخل الوقت بصيغة HH:mm (مثلاً 02:00).",
        ["SettingsBrowserExtension"] = "إضافة المتصفح…",

        ["QueueWhenDone"] = "عند انتهاء كل التنزيلات:",
        ["FinishNothing"] = "لا شيء",
        ["FinishSleep"] = "سكون",
        ["FinishShutDown"] = "إيقاف التشغيل",
        ["FinishQuitApp"] = "إغلاق DownloadMaster",
        ["QueueScheduledFor"] = "مجدول عند {0}",
        ["QueueScheduleWindow"] = "الجدولة: {0}",
        ["QueueScheduleHeld"] = "{0} بانتظار {1}",
        ["QueueStartNow"] = "ابدأ الآن",

        ["FinishCountdownTitle"] = "انتهت كل التنزيلات",
        ["FinishCountdownSleep"] = "سيدخل الكمبيوتر في وضع السكون خلال {0} ثانية.",
        ["FinishCountdownShutDown"] = "سيتم إيقاف تشغيل الكمبيوتر خلال {0} ثانية.",
        ["FinishCountdownQuitApp"] = "سيتم إغلاق DownloadMaster خلال {0} ثانية.",
        ["FinishCountdownNow"] = "نفّذ الآن",
        ["FinishCountdownCancel"] = "إلغاء",
        ["FinishActionFailed"] = "تعذر تنفيذ الإجراء: {0}",

        ["BatchButton"] = "تنزيل دفعة…",
        ["BatchTitle"] = "تنزيل دفعة",
        ["BatchHint"] = "استخدم نطاقات [من-إلى]: part_[01-20].rar (مع الحفاظ على الأصفار)، img_[a-f].jpg (أحرف)، shot_[0-100:5].png (بخطوة 5). النطاقات المتعددة تتضاعف.",
        ["BatchPatternPlaceholder"] = "https://example.com/part_[01-20].rar",
        ["BatchPreview"] = "معاينة",
        ["BatchCount"] = "{0} رابط",
        ["BatchMore"] = "… و{0} أخرى",
        ["BatchNoPattern"] = "أضف نطاقاً مثل [1-10] أو [a-f] إلى الرابط.",
        ["BatchInvalid"] = "نمط غير صالح: {0}",
        ["BatchInvalidUrl"] = "يجب أن تبدأ الروابط بـ http:// أو https://.",
        ["BatchQueueAll"] = "تنزيل الكل",
        ["BatchQueued"] = "تمت إضافة {0} تنزيل إلى القائمة.",

        ["CopySha256"] = "نسخ SHA-256",
        ["CopySha256Copied"] = "تم نسخ SHA-256 إلى الحافظة.",
    };
}
