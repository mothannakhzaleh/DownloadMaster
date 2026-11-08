using System.Globalization;
using DownloadMaster.Models;

namespace DownloadMaster.Services.Speech;

/// <summary>
/// Linux fallback when espeak-ng is missing but speech-dispatcher's <c>spd-say</c> exists.
/// It can only speak aloud (no file output), so <see cref="CanExport"/> is false.
/// Rate → <c>-r</c> (-100..100), volume → <c>-i</c> (-100..100), voice → <c>-y</c>.
/// </summary>
internal sealed class SpeechDispatcherEngine(string spdSayPath) : ISpeechEngine
{
    private readonly LiveSpeechSlot _live = new();

    public SpeechEngineKind Kind => SpeechEngineKind.SpeechDispatcher;
    public string DisplayName => "Speech Dispatcher (spd-say)";
    public bool CanExport => false;

    public async Task<IReadOnlyList<SpeechVoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken)
    {
        var voices = new List<SpeechVoiceInfo>
        {
            new() { Name = string.Empty, DisplayName = "Default" }
        };

        SpeechProcessResult result;
        try
        {
            result = await SpeechProcess.RunAsync(spdSayPath, ["-L"], null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return voices;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in result.StandardOutput.Split('\n'))
        {
            // "            Afrikaans                       af                none"
            var parts = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || (parts.Contains("NAME") && parts.Contains("LANGUAGE")))
                continue;

            var name = string.Join(' ', parts[..^2]);
            if (!seen.Add(name))
                continue;

            voices.Add(new SpeechVoiceInfo
            {
                Name = name,
                DisplayName = name,
                Locale = parts[^2],
                CultureDisplay = SpeechProcess.DescribeLocale(parts[^2])
            });
        }

        return voices;
    }

    public Task SpeakAsync(string text, string voiceName, int rate, int volume, CancellationToken cancellationToken) =>
        _live.RunAsync(cancellationToken, async token =>
        {
            var args = new List<string> { "-w" };
            if (!string.IsNullOrWhiteSpace(voiceName))
            {
                args.Add("-y");
                args.Add(voiceName);
            }

            args.Add("-r");
            args.Add((Math.Clamp(rate, -10, 10) * 10).ToString(CultureInfo.InvariantCulture));
            args.Add("-i");
            args.Add((Math.Clamp(volume, 0, 100) * 2 - 100).ToString(CultureInfo.InvariantCulture));
            args.Add("--");
            args.Add(text);

            try
            {
                await SpeechProcess.RunCheckedAsync(spdSayPath, args, null, token);
            }
            catch (OperationCanceledException)
            {
                CancelQueuedSpeech();
                throw;
            }
        });

    public void Stop()
    {
        _live.Stop();
        CancelQueuedSpeech();
    }

    public Task SynthesizeToWavAsync(string text, string voiceName, int rate, int volume, string wavPath, CancellationToken cancellationToken) =>
        throw new NotSupportedException("spd-say cannot write audio files. Install espeak-ng to export.");

    /// <summary>Killing spd-say does not stop the speech-dispatcher daemon, so tell it to cancel.</summary>
    private void CancelQueuedSpeech()
    {
        try
        {
            _ = SpeechProcess.RunAsync(spdSayPath, ["-C"], null, CancellationToken.None)
                .ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
        catch
        {
            // best effort
        }
    }

    public void Dispose() => Stop();
}
