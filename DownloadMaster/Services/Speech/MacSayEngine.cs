using System.Globalization;
using System.Text.RegularExpressions;
using DownloadMaster.Models;

namespace DownloadMaster.Services.Speech;

/// <summary>
/// macOS built-in <c>say</c>. Voices: <c>say -v ?</c>. Rate → <c>-r</c> words per minute.
/// Volume → the <c>[[volm x]]</c> embedded speech command. Export writes 16-bit WAV directly.
/// </summary>
internal sealed partial class MacSayEngine(string sayPath) : ISpeechEngine
{
    private const int NormalWordsPerMinute = 180;

    private readonly LiveSpeechSlot _live = new();

    public SpeechEngineKind Kind => SpeechEngineKind.MacSay;
    public string DisplayName => "macOS say";
    public bool CanExport => true;

    // "Eddy (English (UK)) en_GB    # Hello! My name is Eddy."
    [GeneratedRegex(@"^(?<name>.+?)\s+(?<locale>[a-z]{2,3}(?:[_-][A-Za-z0-9]+)*)\s+#\s?(?<sample>.*)$")]
    private static partial Regex VoiceLine();

    public async Task<IReadOnlyList<SpeechVoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken)
    {
        var result = await SpeechProcess.RunCheckedAsync(sayPath, ["-v", "?"], null, cancellationToken);
        var voices = new List<SpeechVoiceInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawLine in result.StandardOutput.Split('\n'))
        {
            var match = VoiceLine().Match(rawLine.TrimEnd('\r'));
            if (!match.Success)
                continue;

            var name = match.Groups["name"].Value.Trim();
            if (name.Length == 0 || !seen.Add(name))
                continue;

            voices.Add(new SpeechVoiceInfo
            {
                Name = name,
                DisplayName = name,
                Locale = match.Groups["locale"].Value,
                CultureDisplay = SpeechProcess.DescribeLocale(match.Groups["locale"].Value)
            });
        }

        return voices
            .OrderBy(voice => voice.CultureDisplay, StringComparer.OrdinalIgnoreCase)
            .ThenBy(voice => voice.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task SpeakAsync(string text, string voiceName, int rate, int volume, CancellationToken cancellationToken) =>
        _live.RunAsync(cancellationToken, token =>
            SpeechProcess.RunCheckedAsync(sayPath, BuildArguments(voiceName, rate, outputPath: null), PrepareText(text, volume), token));

    public void Stop() => _live.Stop();

    public async Task SynthesizeToWavAsync(
        string text,
        string voiceName,
        int rate,
        int volume,
        string wavPath,
        CancellationToken cancellationToken)
    {
        await SpeechProcess.RunCheckedAsync(sayPath, BuildArguments(voiceName, rate, wavPath), PrepareText(text, volume), cancellationToken);

        if (!File.Exists(wavPath))
            throw new InvalidOperationException("say did not create an audio file.");
    }

    private static List<string> BuildArguments(string voiceName, int rate, string? outputPath)
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(voiceName))
        {
            args.Add("-v");
            args.Add(voiceName);
        }

        args.Add("-r");
        args.Add(SpeechScale.WordsPerMinute(rate, NormalWordsPerMinute, 60, 500).ToString(CultureInfo.InvariantCulture));

        if (outputPath is not null)
        {
            // WAVE needs an explicit little-endian PCM data format, otherwise say fails with "fmt?".
            args.Add("-o");
            args.Add(outputPath);
            args.Add("--file-format=WAVE");
            args.Add("--data-format=LEI16@22050");
        }

        // Text comes from stdin so it is never parsed as options and has no length limit.
        args.Add("-f");
        args.Add("-");
        return args;
    }

    private static string PrepareText(string text, int volume)
    {
        var level = Math.Clamp(volume, 0, 100);
        return level >= 100
            ? text
            : $"[[volm {(level / 100.0).ToString("0.00", CultureInfo.InvariantCulture)}]] {text}";
    }

    public void Dispose() => Stop();
}
