namespace DownloadMaster.Models;

public enum SpeechOutputFormat
{
    Wav,
    Mp3,
    M4a
}

/// <summary>Text-to-speech backend picked for the current OS.</summary>
public enum SpeechEngineKind
{
    /// <summary>No engine installed (e.g. Linux without espeak-ng).</summary>
    None,

    /// <summary>Windows: System.Speech (SAPI voices).</summary>
    Windows,

    /// <summary>macOS: the built-in <c>say</c> command.</summary>
    MacSay,

    /// <summary>Linux: <c>espeak-ng</c> (or legacy <c>espeak</c>).</summary>
    ESpeak,

    /// <summary>Linux: speech-dispatcher's <c>spd-say</c> (speak aloud only, no file export).</summary>
    SpeechDispatcher
}

public sealed class SpeechVoiceInfo
{
    /// <summary>Engine-specific id passed back to the engine (SAPI name, say voice, espeak language code).</summary>
    public required string Name { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Language tag as reported by the engine ("en-US", "en_GB", "ar"); may be empty.</summary>
    public string Locale { get; init; } = string.Empty;
    public string CultureDisplay { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;

    public string DetailText => string.IsNullOrWhiteSpace(Gender)
        ? CultureDisplay
        : string.IsNullOrWhiteSpace(CultureDisplay)
            ? Gender
            : $"{CultureDisplay}  •  {Gender}";
}

public sealed class SpeechConversionSettings
{
    public required string Text { get; init; }
    public required string VoiceName { get; init; }

    /// <summary>Speed on the SAPI scale (-10..10, 0 = normal). Mapped per engine.</summary>
    public int Rate { get; init; }

    /// <summary>Volume 0..100. Mapped per engine.</summary>
    public int Volume { get; init; } = 100;
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
