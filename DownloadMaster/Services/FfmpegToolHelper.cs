using System.IO;
using FFMpegCore;

namespace DownloadMaster.Services;

public static class FfmpegToolHelper
{
    private static bool _configured;

    public static string BinaryFolder =>
        ToolLocator.FfmpegFolder
        ?? throw new InvalidOperationException("FFmpeg is not configured.");

    public static string FfmpegExePath => Path.Combine(BinaryFolder, "ffmpeg.exe");

    public static bool TryConfigure()
    {
        if (_configured)
            return ToolLocator.HasFfmpeg;

        ToolLocator.ConfigureBundled();
        if (!ToolLocator.HasFfmpeg)
            return false;

        GlobalFFOptions.Configure(new FFOptions
        {
            BinaryFolder = ToolLocator.FfmpegFolder!,
            TemporaryFilesFolder = Path.GetTempPath()
        });

        _configured = true;
        return true;
    }

    public static void EnsureAvailable()
    {
        if (!TryConfigure())
        {
            throw new InvalidOperationException(
                "FFmpeg was not found. Run setup-tools.bat or place ffmpeg.exe and ffprobe.exe in DownloadMaster\\tools\\ffmpeg\\.");
        }
    }
}
