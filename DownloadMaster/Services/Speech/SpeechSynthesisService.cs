using System.Buffers.Binary;
using DownloadMaster.Models;
using DownloadMaster.Services.Speech;
using FFMpegCore;

namespace DownloadMaster.Services;

/// <summary>
/// Cross-platform text-to-speech: Windows (System.Speech), macOS (<c>say</c>), Linux (<c>espeak-ng</c>,
/// or <c>spd-say</c> for speak-only). Every engine renders WAV; MP3/M4A are encoded with FFmpeg.
/// </summary>
public sealed class SpeechSynthesisService : IDisposable
{
    private readonly ISpeechEngine _engine;
    private CancellationTokenSource? _previewCancellation;
    private readonly object _sync = new();

    public SpeechSynthesisService()
    {
        _engine = SpeechEngineFactory.Create();
    }

    /// <summary>Which backend is in use (drives the install hints shown in the UI).</summary>
    public SpeechEngineKind EngineKind => _engine.Kind;

    /// <summary>Engine name for the UI, e.g. "Windows Speech", "macOS say", "eSpeak NG".</summary>
    public string EngineName => _engine.DisplayName;

    /// <summary>False when no TTS engine is installed.</summary>
    public bool IsAvailable => _engine.Kind != SpeechEngineKind.None;

    /// <summary>False for speak-only engines (spd-say) or when nothing is installed.</summary>
    public bool CanExport => _engine.CanExport;

    public Task<IReadOnlyList<SpeechVoiceInfo>> GetInstalledVoicesAsync(CancellationToken cancellationToken = default) =>
        _engine.GetVoicesAsync(cancellationToken);

    public async Task PreviewAsync(
        string text,
        string voiceName,
        int rate,
        int volume,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Please enter text to speak.");

        using var preview = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_sync)
        {
            _previewCancellation?.Cancel();
            _previewCancellation = preview;
        }

        try
        {
            await _engine.SpeakAsync(text, voiceName, rate, volume, preview.Token);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_previewCancellation, preview))
                    _previewCancellation = null;
            }
        }
    }

    public void StopPreview()
    {
        lock (_sync)
            _previewCancellation?.Cancel();

        _engine.Stop();
    }

    public async Task<SpeechConversionResult> ExportAsync(
        SpeechConversionSettings settings,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.Text))
            throw new InvalidOperationException("Please enter text to speak.");

        if (!CanExport)
            throw new InvalidOperationException($"{EngineName} cannot export audio files.");

        if (settings.OutputFormat != SpeechOutputFormat.Wav)
            FfmpegToolHelper.EnsureAvailable();

        Directory.CreateDirectory(settings.OutputFolder);

        var outputPath = GetUniquePath(Path.Combine(
            settings.OutputFolder,
            SanitizeFileName(settings.OutputBaseName) + GetExtension(settings.OutputFormat)));

        var tempWav = Path.Combine(Path.GetTempPath(), $"spconv_{Guid.NewGuid():N}.wav");

        try
        {
            progress?.Report(10);

            await _engine.SynthesizeToWavAsync(
                settings.Text,
                settings.VoiceName,
                settings.Rate,
                settings.Volume,
                tempWav,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(55);

            if (settings.OutputFormat == SpeechOutputFormat.Wav)
            {
                File.Move(tempWav, outputPath, overwrite: true);
            }
            else
            {
                await ConvertWavAsync(tempWav, outputPath, settings.OutputFormat, cancellationToken);
                TryDelete(tempWav);
            }

            progress?.Report(100);

            return new SpeechConversionResult
            {
                OutputPath = outputPath,
                OutputSizeBytes = new FileInfo(outputPath).Length,
                Duration = await TryGetDurationAsync(outputPath),
                Success = true,
                CharacterCount = settings.Text.Length
            };
        }
        catch
        {
            TryDelete(tempWav);
            if (cancellationToken.IsCancellationRequested)
                TryDelete(outputPath);

            throw;
        }
    }

    private static string GetExtension(SpeechOutputFormat format) => format switch
    {
        SpeechOutputFormat.Mp3 => ".mp3",
        SpeechOutputFormat.M4a => ".m4a",
        _ => ".wav"
    };

    private static string SanitizeFileName(string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return "speech";

        // Path.GetInvalidFileNameChars() is only '/' and '\0' on Unix; also strip Windows-reserved chars
        // so names stay portable.
        foreach (var invalid in Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', '*', '?', '"', '<', '>', '|']))
            trimmed = trimmed.Replace(invalid, '_');

        return trimmed.Trim('.', ' ') is { Length: > 0 } clean ? clean : "speech";
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path)!;
        var baseName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 1; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{baseName}_{index}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, $"{baseName}_{Guid.NewGuid():N}{extension}");
    }

    private static async Task ConvertWavAsync(
        string wavPath,
        string outputPath,
        SpeechOutputFormat format,
        CancellationToken cancellationToken)
    {
        var ffmpeg = ToolLocator.FfmpegExePath
            ?? throw new InvalidOperationException("FFmpeg was not found. " + ToolLocator.InstallHint);

        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", wavPath };
        if (format == SpeechOutputFormat.M4a)
            args.AddRange(["-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart"]);
        else
            args.AddRange(["-c:a", "libmp3lame", "-b:a", "128k"]);
        args.Add(outputPath);

        await SpeechProcess.RunCheckedAsync(ffmpeg, args, null, cancellationToken);

        if (!File.Exists(outputPath))
            throw new InvalidOperationException("FFmpeg did not create the output file.");
    }

    private static async Task<TimeSpan> TryGetDurationAsync(string outputPath)
    {
        try
        {
            if (!File.Exists(outputPath))
                return TimeSpan.Zero;

            if (FfmpegToolHelper.TryConfigure())
            {
                var analysis = await FFProbe.AnalyseAsync(outputPath);
                return analysis.Duration;
            }

            if (outputPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                return ReadWavDuration(outputPath);
        }
        catch
        {
            // Duration is optional for the result summary.
        }

        return TimeSpan.Zero;
    }

    /// <summary>Duration from a RIFF/WAVE header (used when ffprobe is not installed).</summary>
    private static TimeSpan ReadWavDuration(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[12];
        if (stream.Read(header) != 12 || !header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WAVE"u8))
            return TimeSpan.Zero;

        uint byteRate = 0;
        Span<byte> chunk = stackalloc byte[8];
        Span<byte> fmt = stackalloc byte[16];
        while (stream.Read(chunk) == 8)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if (chunk[..4].SequenceEqual("fmt "u8))
            {
                if (size < 16 || stream.Read(fmt) != 16)
                    return TimeSpan.Zero;

                byteRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[8..12]);
                stream.Seek(size - 16 + (size & 1), SeekOrigin.Current);
            }
            else if (chunk[..4].SequenceEqual("data"u8))
            {
                // Streams written without a final size use 0 / 0xFFFFFFFF: fall back to the file length.
                var dataBytes = size is 0 or uint.MaxValue ? stream.Length - stream.Position : size;
                return byteRate == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(dataBytes / (double)byteRate);
            }
            else
            {
                stream.Seek(size + (size & 1), SeekOrigin.Current);
            }
        }

        return TimeSpan.Zero;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        StopPreview();
        _engine.Dispose();
    }
}
