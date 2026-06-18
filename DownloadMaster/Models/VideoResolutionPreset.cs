namespace DownloadMaster.Models;

public sealed class VideoResolutionPreset
{
    public required string Label { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }

    public bool IsSource => Width is null || Height is null;

    public string DimensionsText => IsSource ? string.Empty : $"{Width}x{Height}";

    public string? ToScaleFilterSegment()
    {
        if (IsSource)
            return null;

        return $"scale={Width}:{Height}:flags=lanczos:force_original_aspect_ratio=decrease," +
               $"pad={Width}:{Height}:(ow-iw)/2:(oh-ih)/2:color=black";
    }

    public static VideoResolutionPreset Source { get; } = new()
    {
        Label = "Same as source"
    };

    public static IReadOnlyList<VideoResolutionPreset> Presets { get; } =
    [
        Source,
        new() { Label = "HD 1080p", Width = 1920, Height = 1080 },
        new() { Label = "HD 720p", Width = 1280, Height = 720 },
        new() { Label = "480p", Width = 854, Height = 480 },
        new() { Label = "360p", Width = 640, Height = 360 },
        new() { Label = "240p", Width = 426, Height = 240 },
        new() { Label = "DVD", Width = 720, Height = 576 },
        new() { Label = "TV", Width = 640, Height = 480 },
        new() { Label = "Mobile", Width = 320, Height = 240 }
    ];
}
