using System.IO;

namespace DownloadMaster.Services;

public static class ToolLocator
{
    /// <summary>Bundled tools folder copied next to the exe on build/publish.</summary>
    public static string BundledToolsDir => Path.Combine(AppContext.BaseDirectory, "tools");

    public static string BundledFfmpegDir => Path.Combine(BundledToolsDir, "ffmpeg");

    public static string BundledYtDlpPath => Path.Combine(BundledToolsDir, "yt-dlp.exe");

    private static string? _ffmpegFolder;
    private static string? _ytDlpPath;

    public static string? FfmpegFolder => _ffmpegFolder;
    public static string? YtDlpExecutable => _ytDlpPath;
    public static bool HasFfmpeg => _ffmpegFolder is not null;
    public static bool HasYtDlp => _ytDlpPath is not null;

    public static bool ConfigureBundled()
    {
        _ffmpegFolder = ResolveBundledFfmpeg();
        _ytDlpPath = ResolveBundledYtDlp();
        return _ytDlpPath is not null;
    }

    private static string? ResolveBundledFfmpeg()
    {
        if (File.Exists(Path.Combine(BundledFfmpegDir, "ffmpeg.exe")) &&
            File.Exists(Path.Combine(BundledFfmpegDir, "ffprobe.exe")))
            return BundledFfmpegDir;

        return null;
    }

    private static string? ResolveBundledYtDlp() =>
        File.Exists(BundledYtDlpPath) ? Path.GetFullPath(BundledYtDlpPath) : null;
}
