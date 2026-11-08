using System.IO;
using FFMpegCore;

namespace DownloadMaster.Services;

public static class FfmpegToolHelper
{
    private static bool _configured;

    public static string BinaryFolder =>
        ToolLocator.FfmpegFolder
        ?? throw new InvalidOperationException("FFmpeg is not configured.");

    public static string FfmpegExePath =>
        ToolLocator.FfmpegExePath
        ?? throw new InvalidOperationException("FFmpeg is not configured.");

    public static string FfprobeExePath =>
        ToolLocator.FfprobeExePath
        ?? throw new InvalidOperationException("FFmpeg is not configured.");

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
            throw new InvalidOperationException("FFmpeg was not found. " + ToolLocator.InstallHint);
    }
}
