namespace DownloadMaster.Services.Localization;

/// <summary>Strings for the Speech tab. Keys missing here fall back to LocalizationService's main tables.</summary>
public static class SpeechStrings
{
    public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["SpeechEngineLabel"] = "Engine: {0}",
        ["SpeechLoadingVoices"] = "Loading voices...",
        ["SpeechVoicesLoadFailed"] = "Could not list voices: {0}",
        ["SpeechRefreshVoices"] = "Refresh voices",
        ["SpeechChooseFolder"] = "Select output folder",
        ["SpeechEngineMissingTitle"] = "Speech engine not found",

        // Per-OS hints under the output card.
        ["SpeechHintWindows"] = "Uses Windows built-in voices. Install more in Settings → Time & language → Speech.",
        ["SpeechHintMac"] = "Uses macOS built-in voices (say). Add more in System Settings → Accessibility → Spoken Content → System voice → Manage Voices.",
        ["SpeechHintESpeak"] = "Uses eSpeak NG. More voices: install the espeak-ng-data / mbrola packages from your distribution.",
        ["SpeechHintSpd"] = "Uses Speech Dispatcher (spd-say) for preview only. To export audio files install eSpeak NG: sudo apt install espeak-ng",

        // No voices returned by an installed engine.
        ["SpeechNoVoicesWindows"] = "No voices found. Install a speech language pack in Windows Settings → Time & language → Speech.",
        ["SpeechNoVoicesMac"] = "No voices found. Add voices in System Settings → Accessibility → Spoken Content.",
        ["SpeechNoVoicesLinux"] = "No voices found. Reinstall espeak-ng-data (e.g. sudo apt install --reinstall espeak-ng-data).",

        // No engine installed at all.
        ["SpeechNoEngineWindows"] = "Windows speech is not available on this PC (the Windows speech runtime is missing).",
        ["SpeechNoEngineMac"] = "The macOS 'say' command was not found.",
        ["SpeechNoEngineLinux"] = "No text-to-speech engine was found. Install eSpeak NG, then click Refresh voices:\nUbuntu/Debian: sudo apt install espeak-ng\nFedora: sudo dnf install espeak-ng\nArch: sudo pacman -S espeak-ng",

        ["SpeechExportUnsupported"] = "{0} can only speak aloud. Install eSpeak NG to export audio files: sudo apt install espeak-ng",
    };

    public static readonly IReadOnlyDictionary<string, string> Arabic = new Dictionary<string, string>
    {
        ["SpeechEngineLabel"] = "المحرك: {0}",
        ["SpeechLoadingVoices"] = "جاري تحميل الأصوات...",
        ["SpeechVoicesLoadFailed"] = "تعذر عرض الأصوات: {0}",
        ["SpeechRefreshVoices"] = "تحديث الأصوات",
        ["SpeechChooseFolder"] = "اختر مجلد الإخراج",
        ["SpeechEngineMissingTitle"] = "لم يُعثر على محرك الكلام",

        ["SpeechHintWindows"] = "يستخدم أصوات Windows المدمجة. ثبّت المزيد من الإعدادات → الوقت واللغة → الكلام.",
        ["SpeechHintMac"] = "يستخدم أصوات macOS المدمجة (say). أضف المزيد من إعدادات النظام → تسهيلات الاستخدام → المحتوى المنطوق → صوت النظام → إدارة الأصوات.",
        ["SpeechHintESpeak"] = "يستخدم eSpeak NG. لمزيد من الأصوات ثبّت حزم espeak-ng-data أو mbrola من توزيعتك.",
        ["SpeechHintSpd"] = "يستخدم Speech Dispatcher (spd-say) للمعاينة فقط. لتصدير ملفات الصوت ثبّت eSpeak NG: sudo apt install espeak-ng",

        ["SpeechNoVoicesWindows"] = "لم يُعثر على أصوات. ثبّت حزمة لغة كلام من إعدادات Windows → الوقت واللغة → الكلام.",
        ["SpeechNoVoicesMac"] = "لم يُعثر على أصوات. أضف أصواتًا من إعدادات النظام → تسهيلات الاستخدام → المحتوى المنطوق.",
        ["SpeechNoVoicesLinux"] = "لم يُعثر على أصوات. أعد تثبيت espeak-ng-data (مثلًا sudo apt install --reinstall espeak-ng-data).",

        ["SpeechNoEngineWindows"] = "خدمة الكلام في Windows غير متاحة على هذا الجهاز (مكوّن الكلام مفقود).",
        ["SpeechNoEngineMac"] = "لم يُعثر على أمر say في macOS.",
        ["SpeechNoEngineLinux"] = "لم يُعثر على محرك لتحويل النص إلى كلام. ثبّت eSpeak NG ثم اضغط تحديث الأصوات:\nUbuntu/Debian: sudo apt install espeak-ng\nFedora: sudo dnf install espeak-ng\nArch: sudo pacman -S espeak-ng",

        ["SpeechExportUnsupported"] = "{0} يدعم النطق فقط. ثبّت eSpeak NG لتصدير ملفات الصوت: sudo apt install espeak-ng",
    };
}
