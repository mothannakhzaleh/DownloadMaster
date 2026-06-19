using System.IO;
using DownloadMaster.Models;
using FFMpegCore;

namespace DownloadMaster.Services;

public sealed class AudioProbeService
{
    public async Task<AudioProbeInfo?> ProbeAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        if (!AudioFormatCatalog.IsSupportedAudio(path))
            return null;

        FfmpegToolHelper.EnsureAvailable();

        var analysis = await FFProbe.AnalyseAsync(path);
        var audio = analysis.PrimaryAudioStream;
        var fileInfo = new FileInfo(path);

        return new AudioProbeInfo
        {
            FileName = Path.GetFileName(path),
            Format = analysis.Format?.FormatName ?? "unknown",
            AudioCodec = audio?.CodecName ?? "none",
            Channels = audio?.Channels ?? 0,
            SampleRateHz = audio?.SampleRateHz ?? 0,
            Duration = analysis.Duration,
            FileSizeBytes = fileInfo.Length,
            BitrateKbps = audio?.BitRate / 1000.0 ?? analysis.Format?.BitRate / 1000.0
        };
    }
}
