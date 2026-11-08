using DownloadMaster.Models;

namespace DownloadMaster.Services.Speech;

/// <summary>
/// One text-to-speech backend. <see cref="SpeechSynthesisService"/> picks the right one per OS
/// (see <see cref="SpeechEngineFactory"/>) and adds the shared export pipeline (WAV → FFmpeg → MP3/M4A).
/// </summary>
internal interface ISpeechEngine : IDisposable
{
    SpeechEngineKind Kind { get; }

    /// <summary>Human-readable engine name, e.g. "eSpeak NG".</summary>
    string DisplayName { get; }

    /// <summary>False when the engine can only speak aloud (spd-say) or nothing is installed.</summary>
    bool CanExport { get; }

    Task<IReadOnlyList<SpeechVoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken);

    /// <summary>Speak through the default audio device; completes when speech ends. Cancel = stop.</summary>
    Task SpeakAsync(string text, string voiceName, int rate, int volume, CancellationToken cancellationToken);

    /// <summary>Stop any live speech started by <see cref="SpeakAsync"/>.</summary>
    void Stop();

    /// <summary>Write speech to a PCM WAV file at <paramref name="wavPath"/>.</summary>
    Task SynthesizeToWavAsync(
        string text,
        string voiceName,
        int rate,
        int volume,
        string wavPath,
        CancellationToken cancellationToken);
}

/// <summary>Maps the UI's SAPI-style sliders to other engines' units.</summary>
internal static class SpeechScale
{
    /// <summary>SAPI rate is roughly 3x faster at +10 and 3x slower at -10.</summary>
    public static double RateFactor(int rate) => Math.Pow(3.0, Math.Clamp(rate, -10, 10) / 10.0);

    public static int WordsPerMinute(int rate, int normalWpm, int min, int max) =>
        Math.Clamp((int)Math.Round(normalWpm * RateFactor(rate)), min, max);
}

/// <summary>Placeholder when no TTS engine is installed.</summary>
internal sealed class NullSpeechEngine(string reason) : ISpeechEngine
{
    public SpeechEngineKind Kind => SpeechEngineKind.None;
    public string DisplayName => "—";
    public bool CanExport => false;

    public Task<IReadOnlyList<SpeechVoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SpeechVoiceInfo>>([]);

    public Task SpeakAsync(string text, string voiceName, int rate, int volume, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(reason);

    public void Stop()
    {
    }

    public Task SynthesizeToWavAsync(string text, string voiceName, int rate, int volume, string wavPath, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(reason);

    public void Dispose()
    {
    }
}
