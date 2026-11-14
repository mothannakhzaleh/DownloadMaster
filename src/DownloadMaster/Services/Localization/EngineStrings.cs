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

        // After download: antivirus scan and archive extraction (DownloadItem.ScanNote / ArchiveNote)
        ["ScanRunning"] = "Scanning for viruses…",
        ["ScanClean"] = "No threats found ({0})",
        ["ScanThreat"] = "THREAT FOUND ({0}) — the file was kept; do not open it",
        ["ScanNoDefender"] = "Not scanned: Windows Defender was not found",
        ["ScanNoClamAv"] = "Not scanned: install ClamAV (clamscan) to scan downloads",
        ["ScanFailed"] = "Antivirus scan failed: {0}",
        ["ArchiveExtracting"] = "Extracting…",
        ["ArchiveExtracted"] = "Extracted to {0}",
        ["ArchiveWaitingForParts"] = "Will extract when every part has finished",
        ["ArchiveNeeds7Zip"] = "Not extracted: install 7-Zip to extract this archive",
        ["ArchiveExtractFailed"] = "Extraction failed: {0}",
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

        ["ScanRunning"] = "جارٍ الفحص بحثاً عن الفيروسات…",
        ["ScanClean"] = "لم يتم العثور على تهديدات ({0})",
        ["ScanThreat"] = "تم العثور على تهديد ({0}) — تم الإبقاء على الملف؛ لا تفتحه",
        ["ScanNoDefender"] = "لم يتم الفحص: لم يتم العثور على Windows Defender",
        ["ScanNoClamAv"] = "لم يتم الفحص: ثبّت ClamAV (clamscan) لفحص التنزيلات",
        ["ScanFailed"] = "فشل فحص مكافح الفيروسات: {0}",
        ["ArchiveExtracting"] = "جارٍ فك الضغط…",
        ["ArchiveExtracted"] = "تم فك الضغط إلى {0}",
        ["ArchiveWaitingForParts"] = "سيتم فك الضغط عند اكتمال كل الأجزاء",
        ["ArchiveNeeds7Zip"] = "لم يتم فك الضغط: ثبّت 7-Zip لفك ضغط هذا الأرشيف",
        ["ArchiveExtractFailed"] = "فشل فك الضغط: {0}",
    };
}
