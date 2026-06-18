namespace DownloadMaster.Models;

using System.IO;

public enum ImageConversionMode
{
    SingleFile,
    Folder
}

public enum ImageOutputLocation
{
    SameFolder,
    CustomFolder
}

public enum ImageFormat
{
    Png,
    Jpeg,
    WebP,
    Bmp,
    Tiff,
    Gif,
    Tga,
    Dds
}

public enum ImageResolutionMode
{
    Original,
    Preset
}

public sealed class ImageResolutionPreset
{
    public required string Label { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public static ImageResolutionPreset Original { get; } = new() { Label = "Keep original", Width = 0, Height = 0 };

    public static IReadOnlyList<ImageResolutionPreset> Presets { get; } =
    [
        Original,
        new() { Label = "256 × 256", Width = 256, Height = 256 },
        new() { Label = "512 × 512", Width = 512, Height = 512 },
        new() { Label = "1024 × 1024", Width = 1024, Height = 1024 },
        new() { Label = "1920 × 1080 (HD)", Width = 1920, Height = 1080 }
    ];
}

public sealed class ImageInputFileItem
{
    public required string FullPath { get; init; }
    public string FileName => Path.GetFileName(FullPath);
    public string Details { get; init; } = string.Empty;
}

public sealed class ImageConversionSettings
{
    public ImageConversionMode Mode { get; init; } = ImageConversionMode.SingleFile;
    public required string InputPath { get; init; }
    public ImageFormat TargetFormat { get; init; } = ImageFormat.Png;
    public ImageOutputLocation OutputLocation { get; init; } = ImageOutputLocation.SameFolder;
    public string? OutputFolder { get; init; }
    public bool OptimizeSize { get; init; } = true;
    public int PngColorCount { get; init; } = 256;
    public bool PngDither { get; init; }
    public int JpegQuality { get; init; } = 85;
    public int WebPQuality { get; init; } = 85;
    public bool IncludeSubfolders { get; init; }
    public ImageResolutionMode ResolutionMode { get; init; } = ImageResolutionMode.Original;
    public int TargetWidth { get; init; }
    public int TargetHeight { get; init; }
    public int MaxParallelConversions { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);
}

public sealed class ImageConversionResult
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
