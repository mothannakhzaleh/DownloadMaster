using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed partial class YtDlpService
{
    private static readonly Dictionary<string, int> QualityMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["144p"] = 144, ["240p"] = 240, ["360p"] = 360, ["480p"] = 480,
        ["720p"] = 720, ["1080p"] = 1080, ["1440p"] = 1440, ["2160p"] = 2160, ["best"] = 9999
    };

    public async Task<VideoInfo> FetchInfoAsync(
        string url,
        string preferredFormat = "mp4",
        InstagramCookieSession? instagram = null,
        bool noPlaylist = false,
        CancellationToken ct = default,
        string? cookieFile = null,
        int? playlistItemIndex = null)
    {
        EnsureYtDlp();
        var args = BuildBaseArgs(instagram, noPlaylist, cookieFile, url);
        if (playlistItemIndex is int index)
            args += $" --playlist-items {index}";
        args += $" --dump-single-json --skip-download \"{url}\"";
        var json = await RunCaptureAsync(args, ct);
        json = ExtractJsonPayload(json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var info = new VideoInfo
        {
            Title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
            Thumbnail = root.TryGetProperty("thumbnail", out var th) ? th.GetString() ?? "" : "",
            Duration = TryReadDurationSeconds(root),
            Uploader = root.TryGetProperty("uploader", out var u) ? u.GetString() ?? "" : "",
        };

        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            info.IsPlaylist = true;
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Null) continue;
                info.Entries.Add(new VideoInfoEntry
                {
                    Url = entry.TryGetProperty("url", out var eu) ? eu.GetString() ?? entry.GetProperty("webpage_url").GetString() ?? "" : "",
                    Title = entry.TryGetProperty("title", out var et) ? et.GetString() ?? "" : ""
                });
            }
            info.PlaylistCount = info.Entries.Count;
        }
        else
        {
            VideoFormatAnalyzer.PopulateFromJson(info, root, preferredFormat);
        }

        return info;
    }

    public async Task DownloadAsync(
        DownloadItem item,
        AppSettings settings,
        IProgress<DownloadProgressReport>? progress,
        InstagramCookieSession? instagram = null,
        CancellationToken ct = default)
    {
        EnsureYtDlp();
        Directory.CreateDirectory(item.SaveFolder);

        var height = QualityMap.GetValueOrDefault(item.Quality, 1080);
        var outTemplate = FormatHelpers.ApplyTemplate(settings.NamingTemplate, item.Title, item.Quality, item.Format);
        if (!outTemplate.Contains("%(ext)s", StringComparison.Ordinal))
            outTemplate = Path.GetFileNameWithoutExtension(outTemplate) + ".%(ext)s";

        var output = Path.Combine(item.SaveFolder, outTemplate);
        if (item.IsInstagram && !item.NoPlaylist && item.PlaylistItemIndex is null)
        {
            var baseName = Path.GetFileNameWithoutExtension(output);
            var dir = Path.GetDirectoryName(output) ?? item.SaveFolder;
            output = Path.Combine(dir, baseName + "_%(playlist_index)02d.%(ext)s");
        }

        var format = item.IsInstagram
            ? "best[ext=mp4]/best[ext=jpg]/best[ext=webp]/best[ext=png]/best"
            : $"bestvideo[height<={height}]+bestaudio/best[height<={height}]/best";

        var args = new StringBuilder(BuildBaseArgs(instagram, item.NoPlaylist, item.InstagramCookieFile, item.Url));
        if (item.IsInstagram)
            args.Append(" --ignore-no-formats-error");
        if (item.PlaylistItemIndex is int playlistItem)
            args.Append($" --playlist-items {playlistItem}");
        args.Append(" --newline --no-warnings --continue");
        args.Append($" -f \"{format}\"");
        args.Append($" -o \"{output}\"");
        if (settings.DownloadSubtitles)
            args.Append(" --write-subs --write-auto-subs");
        if (settings.SpeedLimitKbps > 0)
            args.Append($" --limit-rate {settings.SpeedLimitKbps}K");
        args.Append($" \"{item.Url}\"");

        await RunWithProgressAsync(args.ToString(), item, progress, ct);
    }

    private static int TryReadDurationSeconds(JsonElement root)
    {
        if (!root.TryGetProperty("duration", out var duration))
            return 0;

        if (duration.TryGetDouble(out var seconds))
            return (int)Math.Round(seconds);

        if (duration.TryGetInt32(out var whole))
            return whole;

        return 0;
    }

    private static string ExtractJsonPayload(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return output;

        var start = output.IndexOf('{');
        var end = output.LastIndexOf('}');
        if (start >= 0 && end > start)
            return output[start..(end + 1)];

        return output.Trim();
    }

    private static string BuildBaseArgs(
        InstagramCookieSession? instagram = null,
        bool noPlaylist = false,
        string? cookieFile = null,
        string? url = null)
    {
        var sb = new StringBuilder();
        if (ToolLocator.FfmpegFolder is not null)
            sb.Append($" --ffmpeg-location \"{ToolLocator.FfmpegFolder}\"");
        if (instagram is not null)
            sb.Append(instagram.GetYtDlpArguments());
        else if (!string.IsNullOrWhiteSpace(cookieFile))
            sb.Append($" --cookies \"{cookieFile}\"");
        if (noPlaylist)
            sb.Append(" --no-playlist");
        if (instagram is not null || (!string.IsNullOrWhiteSpace(url) && InstagramUrlHelper.IsInstagramUrl(url)))
        {
            sb.Append(" --ignore-no-formats-error");
            sb.Append(" --referer \"https://www.instagram.com/\"");
        }
        return sb.ToString();
    }

    private static void EnsureYtDlp()
    {
        if (ToolLocator.YtDlpExecutable is null)
            throw new FileNotFoundException(
                "yt-dlp.exe not found. Download it from https://github.com/yt-dlp/yt-dlp/releases and place it in the tools\\ folder next to DownloadMaster.exe");
    }

    private async Task RunWithProgressAsync(
        string arguments,
        DownloadItem item,
        IProgress<DownloadProgressReport>? progress,
        CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ToolLocator.YtDlpExecutable!,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };

        process.Start();
        var stderr = new StringBuilder();

        var readOut = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
            {
                if (TryParseProgress(line, out var report))
                    progress?.Report(report);
            }
        }, ct);

        var readErr = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(ct) is { } line)
                stderr.AppendLine(line);
        }, ct);

        await Task.WhenAll(readOut, readErr);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(stderr.ToString().Trim());

        item.OutputPath = FindLatestFile(item.SaveFolder) ?? item.OutputPath;
    }

    private static string? FindLatestFile(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        return Directory.GetFiles(folder)
            .Where(path => !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".temp", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static bool TryParseProgress(string line, out DownloadProgressReport report)
    {
        report = default;
        var match = ProgressRegex().Match(line);
        if (!match.Success) return false;

        report = new DownloadProgressReport
        {
            Percent = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            TotalText = match.Groups[2].Value.Trim(),
            SpeedText = match.Groups[3].Value.Trim(),
            EtaText = match.Groups[4].Value.Trim()
        };
        return true;
    }

    private static async Task<string> RunCaptureAsync(string arguments, CancellationToken ct)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = ToolLocator.YtDlpExecutable!,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Failed to start yt-dlp");

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(error.Trim());

        return output;
    }

    [GeneratedRegex(@"\[download\]\s+([\d.]+)%.*?at\s+(.+?)\s+ETA\s+(.+)", RegexOptions.Compiled)]
    private static partial Regex ProgressRegex();
}

public readonly record struct DownloadProgressReport
{
    public double Percent { get; init; }
    public string TotalText { get; init; }
    public string SpeedText { get; init; }
    public string EtaText { get; init; }
}
