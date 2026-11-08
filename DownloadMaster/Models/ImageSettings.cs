using ImageMagick;

namespace DownloadMaster.Models;

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
    Preset,
    Custom
}

public enum ImageResizeFilter
{
    Smooth,
    NearestNeighbor,
    Bilinear,
    Bicubic
}

public enum ImageColorDepthMode
{
    Default,
    RemoveAlpha,
    Indexed8Bit,
    Keep32BitRgba
}

public enum ImageGameTransparencyMode
{
    Off,
    SatisfyPinkKey
}

/// <summary>
/// How strictly Satisfy / PKO magenta chroma-key pixels are matched.
/// Soft Magick fuzz was punching holes in real pink/purple artwork.
/// </summary>
public enum PinkKeyMatchMode
{
    /// <summary>#FF00FF / #FF01FF / #FF02FF only.</summary>
    Exact,

    /// <summary>
    /// Classic PKO key family: R and B nearly max, G nearly zero.
    /// Safe default — does not match artistic pinks/purples.
    /// </summary>
    NearKey,

    /// <summary>Old Magick ColorFuzz (~1.5%). Can damage real artwork.</summary>
    LegacySoftFuzz
}

public static class ImageResizeFilterInfo
{
    public static FilterType ToMagickFilter(ImageResizeFilter filter) => filter switch
    {
        ImageResizeFilter.NearestNeighbor => FilterType.Point,
        ImageResizeFilter.Bilinear => FilterType.Triangle,
        ImageResizeFilter.Bicubic => FilterType.Cubic,
        _ => FilterType.Lanczos
    };
}

public sealed class ImageResolutionPreset
{
    public required string Label { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public override string ToString() => Label;

    public static IReadOnlyList<ImageResolutionPreset> Presets { get; } =
    [
        new() { Label = "256 × 256", Width = 256, Height = 256 },
        new() { Label = "512 × 512", Width = 512, Height = 512 },
        new() { Label = "1024 × 1024", Width = 1024, Height = 1024 },
        new() { Label = "2048 × 2048", Width = 2048, Height = 2048 },
        new() { Label = "1920 × 1080 (HD)", Width = 1920, Height = 1080 },
        new() { Label = "3840 × 2160 (4K)", Width = 3840, Height = 2160 }
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

    /// <summary>Same-folder only: delete e.g. .bmp/.tga after a successful .png write.</summary>
    public bool DeleteSourceWhenExtensionChanges { get; init; }

    public bool OptimizeSize { get; init; } = true;
    public int PngColorCount { get; init; } = 256;
    public bool PngDither { get; init; }
    public int JpegQuality { get; init; } = 85;
    public int WebPQuality { get; init; } = 85;
    public bool IncludeSubfolders { get; init; }
    public ImageResolutionMode ResolutionMode { get; init; } = ImageResolutionMode.Original;
    public int TargetWidth { get; init; }
    public int TargetHeight { get; init; }
    public ImageResizeFilter ResizeFilter { get; init; } = ImageResizeFilter.Smooth;
    public ImageColorDepthMode ColorDepthMode { get; init; } = ImageColorDepthMode.Default;
    public ImageGameTransparencyMode GameTransparencyMode { get; init; } = ImageGameTransparencyMode.Off;
    public bool ApplyGameTransparencyToPng { get; init; }
    public PinkKeyMatchMode PinkKeyMatchMode { get; init; } = PinkKeyMatchMode.NearKey;
    public int MaxParallelConversions { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);
    public ImageExtensionFilterSettings ExtensionFilter { get; init; } = ImageExtensionFilterSettings.None;
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
