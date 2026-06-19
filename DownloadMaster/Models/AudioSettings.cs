namespace DownloadMaster.Models;

public enum AudioOperationMode
{
    Convert,
    Optimize
}

public enum AudioOutputLocation
{
    SameFolder,
    CustomFolder
}

public sealed class AudioConversionSettings
{
    public required string InputPath { get; init; }
    public AudioOperationMode OperationMode { get; init; } = AudioOperationMode.Convert;
    public AudioFormat TargetFormat { get; init; } = AudioFormat.Mp3;
    public AudioOutputLocation OutputLocation { get; init; } = AudioOutputLocation.SameFolder;
    public string? OutputFolder { get; init; }
    public AudioQualityPreset Quality { get; init; } = AudioQualityPreset.Standard;
}

public sealed class AudioConversionResult
{
    public required string SourcePath { get; init; }
    public required string OutputPath { get; init; }
    public long OriginalSizeBytes { get; init; }
    public long OutputSizeBytes { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public double SizeReductionPercent =>
        OriginalSizeBytes > 0
            ? (1.0 - (double)OutputSizeBytes / OriginalSizeBytes) * 100.0
            : 0;
}

public sealed class AudioProbeInfo
{
    public string FileName { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string AudioCodec { get; init; } = string.Empty;
    public int Channels { get; init; }
    public int SampleRateHz { get; init; }
    public TimeSpan Duration { get; init; }
    public long FileSizeBytes { get; init; }
    public double? BitrateKbps { get; init; }

    public string Summary
    {
        get
        {
            var channelLabel = Channels switch
            {
                1 => "mono",
                2 => "stereo",
                > 2 => $"{Channels} ch",
                _ => "audio"
            };

            var bitrate = BitrateKbps is > 0
                ? $"{BitrateKbps:0} kbps  •  "
                : string.Empty;

            var sampleRate = SampleRateHz > 0
                ? $"{SampleRateHz / 1000.0:0.#} kHz  •  "
                : string.Empty;

            return $"{bitrate}{sampleRate}{channelLabel}  •  {FormatDuration(Duration)}  •  {FormatBytes(FileSizeBytes)}  •  {AudioCodec}";
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}";

        return $"{duration.Minutes}:{duration.Seconds:D2}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }
}
