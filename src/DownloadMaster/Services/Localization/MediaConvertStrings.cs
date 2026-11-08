namespace DownloadMaster.Services.Localization;

/// <summary>Strings for the Video and Audio Convert tabs. Keys missing here fall back to LocalizationService's main tables.</summary>
public static class MediaConvertStrings
{
    public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        // Shared
        ["MediaFfmpegFound"] = "FFmpeg: {0}",
        ["MediaFfmpegMissing"] = "FFmpeg not found. {0}",
        ["MediaCancelling"] = "Cancelling...",
        ["MediaAllFiles"] = "All files",
        ["MediaSelectOutputFolder"] = "Select output folder",
        ["MediaFinishedSize"] = "Finished — {0} → {1}",
        ["MediaSizeNote"] = " at {0} ({1})",

        // Video
        ["VideoConvertModeOptimizeOverwrite"] = "Optimize — re-encode and overwrite same format",
        ["VideoConvertSelectFileTitle"] = "Select a video file",
        ["VideoConvertFilesFilter"] = "Video files",
        ["VideoConvertFramesHint"] = "Creates a folder of numbered frames (frame_0001.png, …) for textures or sprites.",
        ["VideoConvertFramePng"] = "PNG (recommended)",
        ["VideoConvertSummaryOptimize"] = "Optimizing video in place{0}...",
        ["VideoConvertSummaryGif"] = "Creating GIF at {0} FPS{1}...",
        ["VideoConvertSummaryFramesEvery"] = "Extracting every frame as {0}{1}...",
        ["VideoConvertSummaryFramesFps"] = "Extracting frames at {0} FPS as {1}{2}...",
        ["VideoConvertSummaryConvert"] = "Converting to {0}{1}...",
        ["VideoConvertFailedTitle"] = "Video conversion failed",

        // Audio
        ["AudioConvertModeOptimizeOverwrite"] = "Optimize — re-encode and overwrite same format",
        ["AudioConvertFilesFilter"] = "Audio and video files",
        ["AudioConvertSummaryOptimize"] = "Optimizing {0} in place{1}...",
        ["AudioConvertSummaryConvert"] = "Converting to {0}{1}...",
        ["AudioConvertRingtoneNote"] = "iPhone ringtones are trimmed to the first 40 seconds.",
        ["AudioConvertAmrNote"] = "AMR is saved as 8 kHz mono (phone voice quality).",
        ["AudioConvertFailedTitle"] = "Audio conversion failed",
    };

    public static readonly IReadOnlyDictionary<string, string> Arabic = new Dictionary<string, string>
    {
        // Shared
        ["MediaFfmpegFound"] = "FFmpeg: {0}",
        ["MediaFfmpegMissing"] = "لم يتم العثور على FFmpeg. {0}",
        ["MediaCancelling"] = "جاري الإلغاء...",
        ["MediaAllFiles"] = "كل الملفات",
        ["MediaSelectOutputFolder"] = "اختر مجلد الإخراج",
        ["MediaFinishedSize"] = "اكتمل — {0} ← {1}",
        ["MediaSizeNote"] = " بدقة {0} ({1})",

        // Video
        ["VideoConvertModeOptimizeOverwrite"] = "تحسين — إعادة ترميز واستبدال بنفس الصيغة",
        ["VideoConvertSelectFileTitle"] = "اختر ملف فيديو",
        ["VideoConvertFilesFilter"] = "ملفات الفيديو",
        ["VideoConvertFramesHint"] = "ينشئ مجلداً من الإطارات المرقمة (frame_0001.png، …) للنسيج أو sprites.",
        ["VideoConvertFramePng"] = "PNG (موصى به)",
        ["VideoConvertSummaryOptimize"] = "جاري تحسين الفيديو في مكانه{0}...",
        ["VideoConvertSummaryGif"] = "جاري إنشاء GIF بمعدل {0} FPS{1}...",
        ["VideoConvertSummaryFramesEvery"] = "جاري استخراج كل الإطارات بصيغة {0}{1}...",
        ["VideoConvertSummaryFramesFps"] = "جاري استخراج الإطارات بمعدل {0} FPS بصيغة {1}{2}...",
        ["VideoConvertSummaryConvert"] = "جاري التحويل إلى {0}{1}...",
        ["VideoConvertFailedTitle"] = "فشل تحويل الفيديو",

        // Audio
        ["AudioConvertModeOptimizeOverwrite"] = "تحسين — إعادة ترميز واستبدال بنفس الصيغة",
        ["AudioConvertFilesFilter"] = "ملفات الصوت والفيديو",
        ["AudioConvertSummaryOptimize"] = "جاري تحسين {0} في مكانه{1}...",
        ["AudioConvertSummaryConvert"] = "جاري التحويل إلى {0}{1}...",
        ["AudioConvertRingtoneNote"] = "تُقص نغمات iPhone إلى أول 40 ثانية.",
        ["AudioConvertAmrNote"] = "يُحفظ AMR بتردد 8 كيلوهرتز أحادي (جودة صوت الهاتف).",
        ["AudioConvertFailedTitle"] = "فشل تحويل الصوت",
    };
}
