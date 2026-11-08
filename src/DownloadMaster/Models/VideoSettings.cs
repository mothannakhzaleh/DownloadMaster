namespace DownloadMaster.Models;

public enum VideoOperationMode
{
    Convert,
    Optimize,
    ToGif,
    ToFrameTextures
}

public enum FrameTextureFormat
{
    Png,
    Jpg,
    Bmp,
    Tga
}

public enum FrameExtractMode
{
    EveryFrame,
    TargetFps
}

public enum VideoOutputLocation
{
    SameFolder,
    CustomFolder
}

public sealed class VideoConversionSettings
{
    public required string InputPath { get; init; }
    public VideoOperationMode OperationMode { get; init; } = VideoOperationMode.Convert;
    public VideoFormat TargetFormat { get; init; } = VideoFormat.Mp4;
    public VideoOutputLocation OutputLocation { get; init; } = VideoOutputLocation.SameFolder;
    public string? OutputFolder { get; init; }
    public int GifFps { get; init; } = 10;
    public FrameTextureFormat FrameTextureFormat { get; init; } = FrameTextureFormat.Png;
    public FrameExtractMode FrameExtractMode { get; init; } = FrameExtractMode.EveryFrame;
    public int FrameExtractFps { get; init; } = 12;
    public VideoResolutionPreset Resolution { get; init; } = VideoResolutionPreset.Source;
}

public sealed class VideoConversionResult
{
    public required string SourcePath { get; init; }
    public required string OutputPath { get; init; }
    public long OriginalSizeBytes { get; init; }
    public long OutputSizeBytes { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int? ExtractedFrameCount { get; init; }

    public double SizeReductionPercent =>
        OriginalSizeBytes > 0
            ? (1.0 - (double)OutputSizeBytes / OriginalSizeBytes) * 100.0
            : 0;
}

public sealed class VideoProbeInfo
{
    public string FileName { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string VideoCodec { get; init; } = string.Empty;
    public string AudioCodec { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public TimeSpan Duration { get; init; }
    public long FileSizeBytes { get; init; }
    public double? BitrateKbps { get; init; }

    public string Summary =>
        Width > 0
            ? $"{Width} × {Height}  •  {FormatDuration(Duration)}  •  {FormatBytes(FileSizeBytes)}  •  {VideoCodec}/{AudioCodec}"
            : $"{Format}  •  {FormatBytes(FileSizeBytes)}";

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
