namespace DownloadMaster.Models;

public enum SpeechOutputFormat
{
    Wav,
    Mp3,
    M4a
}

public sealed class SpeechVoiceInfo
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public string CultureDisplay { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;

    public string DetailText => string.IsNullOrWhiteSpace(Gender)
        ? CultureDisplay
        : $"{CultureDisplay}  •  {Gender}";
}

public sealed class SpeechConversionSettings
{
    public required string Text { get; init; }
    public required string VoiceName { get; init; }
    public int Rate { get; init; }
    public int Volume { get; init; }
    public SpeechOutputFormat OutputFormat { get; init; } = SpeechOutputFormat.Mp3;
    public required string OutputFolder { get; init; }
    public string OutputBaseName { get; init; } = "speech";
}

public sealed class SpeechConversionResult
{
    public required string OutputPath { get; init; }
    public long OutputSizeBytes { get; init; }
    public TimeSpan Duration { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public int CharacterCount { get; init; }
}
