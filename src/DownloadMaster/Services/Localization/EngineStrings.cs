namespace DownloadMaster.Services.Localization;

/// <summary>Strings for the download engine (segments, speed limit, checksum, refresh address). Keys here override the legacy tables (see LocalizationService.Get).</summary>
public static class EngineStrings
{
    public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["EngineLinkExpired"] = "The download link expired. Download the file again in the browser to refresh the address — the downloaded part is kept.",
        ["EngineLinkExpiredStatus"] = "Link expired",
        ["EngineRefreshAddress"] = "Refresh download address",
        ["EngineRefreshAddressPrompt"] = "This file matches an interrupted download. Continue that download with the new link instead of starting over?",
        ["EngineRefreshAddressContinue"] = "Continue download",
        ["EngineRefreshAddressNew"] = "Download as new file",
        ["EngineAddressRefreshed"] = "Address refreshed, resuming...",
        ["EngineSegments"] = "Connections",
        ["EngineSha256"] = "SHA-256",
        ["EngineSha256Computing"] = "Computing SHA-256...",
        ["EngineCopySha256"] = "Copy SHA-256",
        ["EngineSpeedLimit"] = "Speed limit",
        ["EngineSpeedUnlimited"] = "Unlimited",
        ["EngineSpeed2MB"] = "2 MB/s",
        ["EngineSpeed500KB"] = "500 KB/s",
    };

    public static readonly IReadOnlyDictionary<string, string> Arabic = new Dictionary<string, string>
    {
        ["EngineLinkExpired"] = "انتهت صلاحية رابط التنزيل. نزّل الملف مرة أخرى من المتصفح لتحديث العنوان — الجزء الذي تم تنزيله محفوظ.",
        ["EngineLinkExpiredStatus"] = "انتهت صلاحية الرابط",
        ["EngineRefreshAddress"] = "تحديث عنوان التنزيل",
        ["EngineRefreshAddressPrompt"] = "هذا الملف يطابق تنزيلاً متوقفاً. هل تريد متابعة ذلك التنزيل بالرابط الجديد بدلاً من البدء من جديد؟",
        ["EngineRefreshAddressContinue"] = "متابعة التنزيل",
        ["EngineRefreshAddressNew"] = "تنزيل كملف جديد",
        ["EngineAddressRefreshed"] = "تم تحديث العنوان، جارٍ الاستئناف...",
        ["EngineSegments"] = "الاتصالات",
        ["EngineSha256"] = "SHA-256",
        ["EngineSha256Computing"] = "جارٍ حساب SHA-256...",
        ["EngineCopySha256"] = "نسخ SHA-256",
        ["EngineSpeedLimit"] = "حد السرعة",
        ["EngineSpeedUnlimited"] = "بلا حدود",
        ["EngineSpeed2MB"] = "2 ميغابايت/ث",
        ["EngineSpeed500KB"] = "500 كيلوبايت/ث",
    };
}
