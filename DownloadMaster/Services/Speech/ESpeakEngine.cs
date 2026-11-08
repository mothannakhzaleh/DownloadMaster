using System.Globalization;
using DownloadMaster.Models;

namespace DownloadMaster.Services.Speech;

/// <summary>
/// Linux (or any Unix) <c>espeak-ng</c>, falling back to legacy <c>espeak</c>.
/// Voices: <c>--voices</c>. Rate → <c>-s</c> words per minute (normal 175). Volume → <c>-a</c> amplitude
/// (0..100, where 100 is espeak's default). Live speech plays through espeak itself; export uses <c>-w</c>.
/// </summary>
internal sealed class ESpeakEngine(string executablePath) : ISpeechEngine
{
    private const int NormalWordsPerMinute = 175;

    private readonly LiveSpeechSlot _live = new();

    public SpeechEngineKind Kind => SpeechEngineKind.ESpeak;

    public string DisplayName =>
        Path.GetFileName(executablePath).StartsWith("espeak-ng", StringComparison.Ordinal) ? "eSpeak NG" : "eSpeak";

    public bool CanExport => true;

    public async Task<IReadOnlyList<SpeechVoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken)
    {
        var result = await SpeechProcess.RunCheckedAsync(executablePath, ["--voices"], null, cancellationToken);
        return ParseVoices(result.StandardOutput);
    }

    /// <summary>
    /// espeak-ng: " 5  en-us   --/M   English_(America)   gmw/en-US   (en 10)"
    /// espeak:    " 5  en-us        M  english-us         en-us       (en 3)"
    /// </summary>
    internal static IReadOnlyList<SpeechVoiceInfo> ParseVoices(string output)
    {
        var voices = new List<SpeechVoiceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in output.Split('\n'))
        {
            var parts = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                continue; // header ("Pty Language ...") or blank

            var language = parts[1];
            if (!seen.Add(language))
                continue;

            var gender = parts[2].Length > 0 ? parts[2][^1] : '-';
            voices.Add(new SpeechVoiceInfo
            {
                Name = language,
                DisplayName = parts[3].Replace('_', ' ').Replace('-', ' '),
                Locale = language,
                CultureDisplay = SpeechProcess.DescribeLocale(language),
                Gender = gender switch
                {
                    'M' or 'm' => "Male",
                    'F' or 'f' => "Female",
                    _ => string.Empty
                }
            });
        }

        return voices
            .OrderBy(voice => voice.CultureDisplay, StringComparer.OrdinalIgnoreCase)
            .ThenBy(voice => voice.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task SpeakAsync(string text, string voiceName, int rate, int volume, CancellationToken cancellationToken) =>
        _live.RunAsync(cancellationToken, token =>
            SpeechProcess.RunCheckedAsync(executablePath, BuildArguments(voiceName, rate, volume, wavPath: null), text, token));

    public void Stop() => _live.Stop();

    public async Task SynthesizeToWavAsync(
        string text,
        string voiceName,
        int rate,
        int volume,
        string wavPath,
        CancellationToken cancellationToken)
    {
        await SpeechProcess.RunCheckedAsync(executablePath, BuildArguments(voiceName, rate, volume, wavPath), text, cancellationToken);

        if (!File.Exists(wavPath))
            throw new InvalidOperationException($"{DisplayName} did not create an audio file.");
    }

    private static List<string> BuildArguments(string voiceName, int rate, int volume, string? wavPath)
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(voiceName))
        {
            args.Add("-v");
            args.Add(voiceName);
        }

        args.Add("-s");
        args.Add(SpeechScale.WordsPerMinute(rate, NormalWordsPerMinute, 80, 450).ToString(CultureInfo.InvariantCulture));
        args.Add("-a");
        args.Add(Math.Clamp(volume, 0, 100).ToString(CultureInfo.InvariantCulture));
        args.Add("-b");
        args.Add("1"); // UTF-8 input

        if (wavPath is not null)
        {
            args.Add("-w");
            args.Add(wavPath);
        }

        // Text comes from stdin so it is never parsed as options.
        args.Add("--stdin");
        return args;
    }

    public void Dispose() => Stop();
}
