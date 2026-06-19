using System.IO;

namespace DownloadMaster.Models;

public enum AudioFormat
{
    Mp3,
    Wav,
    IPhoneRingtone,
    M4a,
    Flac,
    Ogg,
    Mp2,
    Amr
}

public sealed class AudioFormatDefinition
{
    public required AudioFormat Format { get; init; }
    public required string ButtonLabel { get; init; }
    public required string DisplayName { get; init; }
    public required string Extension { get; init; }
    public required string AudioCodec { get; init; }
    public bool UsesQuality { get; init; } = true;
    public string? ExtraOutputArgs { get; init; }

    public bool MatchesExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        return Extension.Equals(extension, StringComparison.OrdinalIgnoreCase) ||
               AlternateExtensions.Any(a => a.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> AlternateExtensions { get; init; } = [];
}

public static class AudioFormatCatalog
{
    public static IReadOnlyList<AudioFormatDefinition> OutputFormats { get; } =
    [
        new() { Format = AudioFormat.Mp3, ButtonLabel = "mp3", DisplayName = "MP3", Extension = ".mp3", AudioCodec = "libmp3lame" },
        new() { Format = AudioFormat.Wav, ButtonLabel = "wav", DisplayName = "WAV", Extension = ".wav", AudioCodec = "pcm_s16le", UsesQuality = false },
        new() { Format = AudioFormat.IPhoneRingtone, ButtonLabel = "iPhone ringtone", DisplayName = "iPhone ringtone (M4R)", Extension = ".m4r", AudioCodec = "aac", ExtraOutputArgs = "-movflags +faststart" },
        new() { Format = AudioFormat.M4a, ButtonLabel = "m4a", DisplayName = "M4A (AAC)", Extension = ".m4a", AudioCodec = "aac", ExtraOutputArgs = "-movflags +faststart" },
        new() { Format = AudioFormat.Flac, ButtonLabel = "flac", DisplayName = "FLAC", Extension = ".flac", AudioCodec = "flac", UsesQuality = false, ExtraOutputArgs = "-compression_level 8" },
        new() { Format = AudioFormat.Ogg, ButtonLabel = "ogg", DisplayName = "OGG (Vorbis)", Extension = ".ogg", AudioCodec = "libvorbis" },
        new() { Format = AudioFormat.Mp2, ButtonLabel = "mp2", DisplayName = "MP2", Extension = ".mp2", AudioCodec = "mp2" },
        new() { Format = AudioFormat.Amr, ButtonLabel = "amr", DisplayName = "AMR", Extension = ".amr", AudioCodec = "libopencore_amrnb" }
    ];

    private static readonly HashSet<string> SupportedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".m4a", ".m4r", ".flac", ".ogg", ".opus", ".wma", ".aac",
        ".ac3", ".amr", ".mp2", ".aiff", ".aif", ".caf", ".ape", ".wv", ".mka", ".dts"
    };

    public static bool IsSupportedAudio(string path)
    {
        var extension = Path.GetExtension(path);
        return SupportedAudioExtensions.Contains(extension) ||
               VideoFormatCatalog.IsSupportedVideo(path);
    }

    public static AudioFormatDefinition GetDefinition(AudioFormat format) =>
        OutputFormats.First(f => f.Format == format);

    public static AudioFormatDefinition? ResolveFromExtension(string extension)
    {
        foreach (var definition in OutputFormats)
        {
            if (definition.MatchesExtension(extension))
                return definition;
        }

        return null;
    }

    public static string BuildInputFilter()
    {
        var extensions = SupportedAudioExtensions
            .Concat([
                ".mp4", ".mkv", ".webm", ".avi", ".mov", ".wmv", ".flv", ".mpg", ".mpeg",
                ".m4v", ".ts", ".mts", ".m2ts", ".3gp", ".ogv", ".asf", ".divx", ".vob"
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e)
            .Select(e => $"*{e}");

        return $"Audio and video|{string.Join(";", extensions)}|All files|*.*";
    }
}
