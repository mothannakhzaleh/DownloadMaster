using System.Diagnostics;
using System.IO;
using System.Speech.Synthesis;
using DownloadMaster.Models;
using FFMpegCore;

namespace DownloadMaster.Services;

public sealed class SpeechSynthesisService : IDisposable
{
    private readonly SpeechSynthesizer _synthesizer = new();
    private readonly object _sync = new();

    public IReadOnlyList<SpeechVoiceInfo> GetInstalledVoices()
    {
        lock (_sync)
        {
            return _synthesizer
                .GetInstalledVoices()
                .Where(voice => voice.Enabled)
                .Select(voice =>
                {
                    var info = voice.VoiceInfo;
                    return new SpeechVoiceInfo
                    {
                        Name = info.Name,
                        DisplayName = string.IsNullOrWhiteSpace(info.Description)
                            ? info.Name
                            : info.Description,
                        CultureDisplay = info.Culture.DisplayName,
                        Gender = info.Gender.ToString()
                    };
                })
                .OrderBy(voice => voice.CultureDisplay, StringComparer.OrdinalIgnoreCase)
                .ThenBy(voice => voice.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public async Task PreviewAsync(
        string text,
        string voiceName,
        int rate,
        int volume,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            ConfigureSynthesizer(voiceName, rate, volume);
            _synthesizer.SetOutputToDefaultAudioDevice();
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Completed(object? sender, SpeakCompletedEventArgs e)
        {
            _synthesizer.SpeakCompleted -= Completed;

            if (e.Error is not null)
                tcs.TrySetException(e.Error);
            else if (e.Cancelled)
                tcs.TrySetCanceled(cancellationToken);
            else
                tcs.TrySetResult();
        }

        await using var registration = cancellationToken.Register(() =>
        {
            lock (_sync)
                _synthesizer.SpeakAsyncCancelAll();
        });

        lock (_sync)
        {
            _synthesizer.SpeakCompleted += Completed;
            _synthesizer.SpeakAsync(text);
        }

        await tcs.Task;
    }

    public void StopPreview()
    {
        lock (_sync)
            _synthesizer.SpeakAsyncCancelAll();
    }

    public async Task<SpeechConversionResult> ExportAsync(
        SpeechConversionSettings settings,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.Text))
            throw new InvalidOperationException("Please enter text to speak.");

        if (string.IsNullOrWhiteSpace(settings.VoiceName))
            throw new InvalidOperationException("Please select a voice.");

        Directory.CreateDirectory(settings.OutputFolder);

        var extension = GetExtension(settings.OutputFormat);
        var safeBaseName = SanitizeFileName(settings.OutputBaseName);
        var outputPath = Path.Combine(settings.OutputFolder, safeBaseName + extension);
        outputPath = GetUniquePath(outputPath);

        var tempWav = Path.Combine(Path.GetTempPath(), $"spconv_{Guid.NewGuid():N}.wav");

        try
        {
            progress?.Report(10);

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                lock (_sync)
                {
                    try
                    {
                        ConfigureSynthesizer(settings.VoiceName, settings.Rate, settings.Volume);
                        _synthesizer.SetOutputToWaveFile(tempWav);
                        _synthesizer.Speak(settings.Text);
                    }
                    finally
                    {
                        _synthesizer.SetOutputToNull();
                    }
                }
            }, cancellationToken);

            progress?.Report(55);

            if (settings.OutputFormat == SpeechOutputFormat.Wav)
            {
                File.Move(tempWav, outputPath, overwrite: true);
            }
            else
            {
                FfmpegToolHelper.EnsureAvailable();
                await ConvertWavAsync(tempWav, outputPath, settings.OutputFormat, cancellationToken);
                File.Delete(tempWav);
            }

            progress?.Report(100);

            var fileInfo = new FileInfo(outputPath);
            var duration = await TryGetDurationAsync(outputPath);

            return new SpeechConversionResult
            {
                OutputPath = outputPath,
                OutputSizeBytes = fileInfo.Length,
                Duration = duration,
                Success = true,
                CharacterCount = settings.Text.Length
            };
        }
        catch
        {
            if (File.Exists(tempWav))
            {
                try { File.Delete(tempWav); } catch { /* ignore */ }
            }

            throw;
        }
    }

    private void ConfigureSynthesizer(string voiceName, int rate, int volume)
    {
        _synthesizer.SelectVoice(voiceName);
        _synthesizer.Rate = Math.Clamp(rate, -10, 10);
        _synthesizer.Volume = Math.Clamp(volume, 0, 100);
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

        foreach (var invalid in Path.GetInvalidFileNameChars())
            trimmed = trimmed.Replace(invalid, '_');

        return trimmed;
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
        var codecArgs = format switch
        {
            SpeechOutputFormat.M4a => "-c:a aac -b:a 128k -movflags +faststart",
            _ => "-c:a libmp3lame -b:a 128k"
        };

        var args = $"-hide_banner -y -i \"{wavPath}\" {codecArgs} \"{outputPath}\"";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = FfmpegToolHelper.FfmpegExePath,
                Arguments = args,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}.");
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
        }
        catch
        {
            // Duration is optional for the result summary.
        }

        return TimeSpan.Zero;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _synthesizer.Dispose();
        }
    }
}
