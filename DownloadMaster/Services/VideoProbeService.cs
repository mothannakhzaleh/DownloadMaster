using System.IO;
using FFMpegCore;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed class VideoProbeService
{
    public async Task<VideoProbeInfo?> ProbeAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        if (!VideoFormatCatalog.IsSupportedVideo(path))
            return null;

        FfmpegToolHelper.EnsureAvailable();

        var analysis = await FFProbe.AnalyseAsync(path);
        var video = analysis.PrimaryVideoStream;
        var audio = analysis.PrimaryAudioStream;
        var fileInfo = new FileInfo(path);

        return new VideoProbeInfo
        {
            FileName = Path.GetFileName(path),
            Format = analysis.Format?.FormatName ?? "unknown",
            VideoCodec = video?.CodecName ?? "none",
            AudioCodec = audio?.CodecName ?? "none",
            Width = video?.Width ?? 0,
            Height = video?.Height ?? 0,
            Duration = analysis.Duration,
            FileSizeBytes = fileInfo.Length,
            BitrateKbps = analysis.Format?.BitRate / 1000.0
        };
    }
}
