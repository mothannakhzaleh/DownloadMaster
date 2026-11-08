using System.Runtime.Versioning;
using System.Speech.Synthesis;
using DownloadMaster.Models;

namespace DownloadMaster.Services.Speech;

/// <summary>Windows voices through System.Speech (SAPI). Rate -10..10 and volume 0..100 are native.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsSpeechEngine : ISpeechEngine
{
    private readonly SpeechSynthesizer _synthesizer = new();

    // One utterance at a time: SpeechSynthesizer has a single output target.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SpeechEngineKind Kind => SpeechEngineKind.Windows;
    public string DisplayName => "Windows Speech";
    public bool CanExport => true;

    public Task<IReadOnlyList<SpeechVoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<SpeechVoiceInfo>>(() =>
            _synthesizer
                .GetInstalledVoices()
                .Where(voice => voice.Enabled)
                .Select(voice =>
                {
                    var info = voice.VoiceInfo;
                    return new SpeechVoiceInfo
                    {
                        Name = info.Name,
                        DisplayName = string.IsNullOrWhiteSpace(info.Description) ? info.Name : info.Description,
                        Locale = info.Culture?.Name ?? string.Empty,
                        CultureDisplay = info.Culture?.DisplayName ?? string.Empty,
                        Gender = info.Gender is VoiceGender.NotSet ? string.Empty : info.Gender.ToString()
                    };
                })
                .OrderBy(voice => voice.CultureDisplay, StringComparer.OrdinalIgnoreCase)
                .ThenBy(voice => voice.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(), cancellationToken);

    public async Task SpeakAsync(string text, string voiceName, int rate, int volume, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Configure(voiceName, rate, volume);
            _synthesizer.SetOutputToDefaultAudioDevice();
            await SpeakCoreAsync(text, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Stop() => _synthesizer.SpeakAsyncCancelAll();

    public async Task SynthesizeToWavAsync(
        string text,
        string voiceName,
        int rate,
        int volume,
        string wavPath,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Configure(voiceName, rate, volume);
            _synthesizer.SetOutputToWaveFile(wavPath);
            try
            {
                await SpeakCoreAsync(text, cancellationToken);
            }
            finally
            {
                // Release the wave file handle (and finish its header) before moving or converting it.
                _synthesizer.SetOutputToNull();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Configure(string voiceName, int rate, int volume)
    {
        if (!string.IsNullOrWhiteSpace(voiceName))
            _synthesizer.SelectVoice(voiceName);

        _synthesizer.Rate = Math.Clamp(rate, -10, 10);
        _synthesizer.Volume = Math.Clamp(volume, 0, 100);
    }

    private async Task SpeakCoreAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Completed(object? sender, SpeakCompletedEventArgs e)
        {
            _synthesizer.SpeakCompleted -= Completed;

            if (e.Error is not null)
                tcs.TrySetException(e.Error);
            else if (e.Cancelled)
                tcs.TrySetCanceled(cancellationToken.IsCancellationRequested ? cancellationToken : CancellationToken.None);
            else
                tcs.TrySetResult();
        }

        _synthesizer.SpeakCompleted += Completed;
        _synthesizer.SpeakAsync(text);

        // Registered after SpeakAsync so an already-cancelled token still cancels this prompt.
        await using var registration = cancellationToken.Register(() => _synthesizer.SpeakAsyncCancelAll());
        await tcs.Task;
    }

    public void Dispose()
    {
        try { _synthesizer.SpeakAsyncCancelAll(); } catch { /* ignore */ }
        _synthesizer.Dispose();
        _gate.Dispose();
    }
}
