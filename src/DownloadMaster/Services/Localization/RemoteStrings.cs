namespace DownloadMaster.Services.Localization;

/// <summary>Strings for remote access (phone page, headless server) in Settings. Keys here override the legacy tables.</summary>
public static class RemoteStrings
{
    public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["SettingsRemoteHeader"] = "Remote access",
        ["SettingsRemoteEnabled"] = "Control downloads from a phone or another computer (web page + command-line tool)",
        ["SettingsRemoteNetwork"] = "Allow devices on my home network (otherwise this computer only)",
        ["SettingsRemotePort"] = "Port",
        ["SettingsRemoteCopyLink"] = "Copy pairing link",
        ["SettingsRemoteNewToken"] = "New pairing token",
        ["SettingsRemoteOff"] = "Turn it on and click Save to get a pairing link.",
        ["SettingsRemoteRunning"] = "Running. Open this link on your phone (same Wi-Fi):",
        ["SettingsRemoteRunningLocal"] = "Running on this computer only:",
        ["SettingsRemoteError"] = "Could not start: {0}",
        ["SettingsRemoteTokenChanged"] = "A new token will be used after you click Save; paired devices must pair again.",
        ["SettingsRemoteCopied"] = "Copied. Keep the link private: it gives full control of your downloads.",
        ["SettingsRemoteInvalidPort"] = "Enter a port from 1024 to 65535.",
        ["SettingsRemoteHint"] = "The link contains a secret token. Remote devices can add and control downloads and save only inside your save folder. The connection is not encrypted: use it on networks you trust. Windows may ask to allow DownloadMaster through the firewall."
    };

    public static readonly IReadOnlyDictionary<string, string> Arabic = new Dictionary<string, string>
    {
        ["SettingsRemoteHeader"] = "التحكم عن بُعد",
        ["SettingsRemoteEnabled"] = "التحكم في التنزيلات من الهاتف أو من كمبيوتر آخر (صفحة ويب + أداة سطر الأوامر)",
        ["SettingsRemoteNetwork"] = "السماح للأجهزة على شبكتي المنزلية (وإلا فهذا الكمبيوتر فقط)",
        ["SettingsRemotePort"] = "المنفذ",
        ["SettingsRemoteCopyLink"] = "نسخ رابط الاقتران",
        ["SettingsRemoteNewToken"] = "رمز اقتران جديد",
        ["SettingsRemoteOff"] = "فعّله ثم اضغط حفظ للحصول على رابط الاقتران.",
        ["SettingsRemoteRunning"] = "يعمل. افتح هذا الرابط على هاتفك (نفس شبكة Wi-Fi):",
        ["SettingsRemoteRunningLocal"] = "يعمل على هذا الكمبيوتر فقط:",
        ["SettingsRemoteError"] = "تعذّر التشغيل: {0}",
        ["SettingsRemoteTokenChanged"] = "سيُستخدم رمز جديد بعد الضغط على حفظ، ويجب إعادة اقتران الأجهزة.",
        ["SettingsRemoteCopied"] = "تم النسخ. احتفظ بالرابط سرّيًا: فهو يمنح تحكمًا كاملًا في تنزيلاتك.",
        ["SettingsRemoteInvalidPort"] = "أدخل منفذًا من 1024 إلى 65535.",
        ["SettingsRemoteHint"] = "يحتوي الرابط على رمز سرّي. يمكن للأجهزة البعيدة إضافة التنزيلات والتحكم فيها والحفظ داخل مجلد الحفظ فقط. الاتصال غير مشفّر: استخدمه على شبكات تثق بها. قد يطلب Windows السماح لـ DownloadMaster عبر جدار الحماية."
    };
}
