namespace DownloadMaster.Models;

public sealed class AudioQualityPreset
{
    public required string Label { get; init; }
    public int BitrateKbps { get; init; }

    public string BitrateText => $"{BitrateKbps} kbps";

    public static IReadOnlyList<AudioQualityPreset> Presets { get; } =
    [
        new() { Label = "Economy", BitrateKbps = 64 },
        new() { Label = "Standard", BitrateKbps = 128 },
        new() { Label = "Good", BitrateKbps = 192 },
        new() { Label = "Best", BitrateKbps = 320 }
    ];

    public static AudioQualityPreset Standard { get; } = Presets[1];
}
