using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed class HttpFileDownloadService
{
    private static readonly HttpClient Http = CreateClient();
    private readonly DirectDownloadLinkResolver _resolver = new();

    public async Task<FileDownloadProbeResult> ProbeAsync(string url, CancellationToken ct)
    {
        DirectDownloadLinkResolver.EnsureSupported(url);
        var resolved = await _resolver.ResolveAsync(url, ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, resolved.DownloadUrl);
        if (!resolved.ForceSingleConnection)
            request.Headers.Range = new RangeHeaderValue(0, 0);

        using var response = await SendAsync(request, ct);

        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
            response.EnsureSuccessStatusCode();

        await DirectDownloadLinkResolver.EnsureBinaryResponseAsync(response, ct);
        var probe = BuildProbeResult(resolved.DownloadUrl, response, resolved);

        if (resolved.TotalBytes > 0 && probe.TotalBytes <= 0)
        {
            probe = new FileDownloadProbeResult
            {
                TotalBytes = resolved.TotalBytes,
                SupportsRanges = probe.SupportsRanges,
                FileName = resolved.FileName,
                ContentType = probe.ContentType,
                DownloadUrl = probe.DownloadUrl,
                ForceSingleConnection = probe.ForceSingleConnection
            };
        }

        if (!string.IsNullOrWhiteSpace(resolved.FileName) && IsGenericPageName(probe.FileName))
        {
            probe = new FileDownloadProbeResult
            {
                TotalBytes = probe.TotalBytes,
                SupportsRanges = probe.SupportsRanges,
                FileName = resolved.FileName,
                ContentType = probe.ContentType,
                DownloadUrl = probe.DownloadUrl,
                ForceSingleConnection = probe.ForceSingleConnection
            };
        }

        return probe;
    }

    public async Task DownloadAsync(
        DownloadItem item,
        AppSettings settings,
        IProgress<DownloadProgressReport>? progress,
        Func<Task>? waitIfPausedAsync,
        CancellationToken ct)
    {
        item.AppendDiagnostic($"Starting HTTP download: {item.Url}");
        DirectDownloadLinkResolver.EnsureSupported(item.Url);

        var state = FileDownloadStateStore.Load(item.Id);
        if (state is null)
        {
            var resolved = await _resolver.ResolveAsync(item.Url, ct);
            var probe = await ProbeAsync(item.Url, ct);
            var fileName = ResolveFileName(item, probe.FileName, resolved.FileName);
            var outputPath = GetUniqueOutputPath(item.SaveFolder, fileName);
            var partPath = outputPath + ".part";

            Directory.CreateDirectory(item.SaveFolder);
            if (File.Exists(partPath))
                File.Delete(partPath);

            var totalBytes = probe.TotalBytes > 0 ? probe.TotalBytes : resolved.TotalBytes;
            if (totalBytes > 0)
            {
                using (var fs = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    fs.SetLength(totalBytes);
            }

            var supportsRanges = probe.SupportsRanges && totalBytes > 0 && !resolved.ForceSingleConnection;
            var connections = resolved.ForceSingleConnection
                ? 1
                : Math.Clamp(
                    item.ConnectionCount > 0 ? item.ConnectionCount : settings.FileDownloadConnections,
                    1,
                    16);

            if (!supportsRanges)
                connections = 1;

            state = new FileDownloadState
            {
                Id = item.Id,
                Url = item.Url,
                DownloadUrl = resolved.DownloadUrl,
                OutputPath = outputPath,
                PartPath = partPath,
                TotalBytes = totalBytes,
                SupportsRanges = supportsRanges,
                ConnectionCount = connections,
                Segments = CreateSegments(totalBytes, supportsRanges ? connections : 1)
            };

            FileDownloadStateStore.Save(state);
            item.Title = Path.GetFileName(outputPath);
            item.PartFilePath = partPath;
            item.OutputPath = outputPath;
            item.AppendDiagnostic($"Resolved URL: {resolved.DownloadUrl}");
            item.AppendDiagnostic($"File: {Path.GetFileName(outputPath)} · {FormatHelpers.FormatBytes(totalBytes)} · {state.ConnectionCount} connections");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(state.DownloadUrl))
                state.DownloadUrl = state.Url;

            item.Title = Path.GetFileName(state.OutputPath);
            item.PartFilePath = state.PartPath;
            item.OutputPath = state.OutputPath;
            item.AppendDiagnostic($"Resuming download ({GetCompletedBytes(state)}/{state.TotalBytes} bytes)");
        }

        if (!File.Exists(state.PartPath) && state.TotalBytes > 0)
        {
            using var fs = new FileStream(state.PartPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            fs.SetLength(state.TotalBytes);
        }

        var stopwatch = Stopwatch.StartNew();
        var lastReport = Stopwatch.StartNew();

        await DownloadSegmentsAsync(state, waitIfPausedAsync, () =>
        {
            if (lastReport.ElapsedMilliseconds < 350)
                return;

            lastReport.Restart();
            var completed = GetCompletedBytes(state);
            var elapsed = stopwatch.Elapsed.TotalSeconds;
            var speed = elapsed > 0 ? completed / elapsed : 0;

            var percent = state.TotalBytes > 0
                ? completed * 100.0 / state.TotalBytes
                : completed > 0 ? 50 : 0;

            var remaining = state.TotalBytes > 0 ? state.TotalBytes - completed : 0;
            var eta = speed > 0 && remaining > 0
                ? TimeSpan.FromSeconds(remaining / speed)
                : TimeSpan.Zero;

            progress?.Report(new DownloadProgressReport
            {
                Percent = Math.Min(99.9, percent),
                TotalText = state.TotalBytes > 0
                    ? $"{FormatHelpers.FormatBytes(completed)} / {FormatHelpers.FormatBytes(state.TotalBytes)}"
                    : FormatHelpers.FormatBytes(completed),
                SpeedText = speed > 0 ? FormatHelpers.FormatSpeed(speed) : string.Empty,
                EtaText = eta > TimeSpan.Zero ? eta.ToString(@"hh\:mm\:ss") : string.Empty
            });
        }, ct);

        ValidateOutputFile(state.PartPath);
        FinalizeDownload(state, item);
        progress?.Report(new DownloadProgressReport
        {
            Percent = 100,
            TotalText = state.TotalBytes > 0
                ? FormatHelpers.FormatBytes(state.TotalBytes)
                : FormatHelpers.FormatBytes(new FileInfo(state.OutputPath).Length),
            SpeedText = string.Empty,
            EtaText = string.Empty
        });
    }

    private static async Task DownloadSegmentsAsync(
        FileDownloadState state,
        Func<Task>? waitIfPausedAsync,
        Action reportProgress,
        CancellationToken ct)
    {
        var segmentLock = new object();
        var downloadUrl = string.IsNullOrWhiteSpace(state.DownloadUrl) ? state.Url : state.DownloadUrl;
        var tasks = state.Segments
            .Where(segment => !IsSegmentComplete(segment))
            .Select(segment => DownloadSegmentAsync(downloadUrl, state, segment, segmentLock, waitIfPausedAsync, reportProgress, ct))
            .ToArray();

        if (tasks.Length == 0 && state.TotalBytes > 0 && GetCompletedBytes(state) >= state.TotalBytes)
            return;

        await Task.WhenAll(tasks);
    }

    private static async Task DownloadSegmentAsync(
        string downloadUrl,
        FileDownloadState state,
        FileDownloadSegmentState segment,
        object segmentLock,
        Func<Task>? waitIfPausedAsync,
        Action reportProgress,
        CancellationToken ct)
    {
        while (!IsSegmentComplete(segment))
        {
            ct.ThrowIfCancellationRequested();
            if (waitIfPausedAsync is not null)
                await waitIfPausedAsync();

            var start = segment.Start + segment.Downloaded;
            var end = segment.End;

            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            if (state.SupportsRanges)
                request.Headers.Range = new RangeHeaderValue(start, end);

            using var response = await SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            await DirectDownloadLinkResolver.EnsureBinaryResponseAsync(response, ct);

            await using var network = await response.Content.ReadAsStreamAsync(ct);
            await using var file = new FileStream(
                state.PartPath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite);

            if (state.SupportsRanges)
                file.Seek(start, SeekOrigin.Begin);

            var buffer = new byte[128 * 1024];
            int read;
            while ((read = await network.ReadAsync(buffer, ct)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                if (waitIfPausedAsync is not null)
                    await waitIfPausedAsync();

                await file.WriteAsync(buffer.AsMemory(0, read), ct);

                lock (segmentLock)
                {
                    segment.Downloaded += read;
                    if (segment.Downloaded % (512 * 1024) < read)
                        FileDownloadStateStore.Save(state);
                }

                reportProgress();
            }

            lock (segmentLock)
            {
                if (segment.End == long.MaxValue)
                {
                    segment.Downloaded = file.Length - segment.Start;
                    if (state.TotalBytes <= 0)
                        state.TotalBytes = file.Length;
                }
                else
                {
                    segment.Downloaded = segment.End - segment.Start + 1;
                }

                FileDownloadStateStore.Save(state);
            }
        }
    }

    private static void ValidateOutputFile(string path)
    {
        if (!File.Exists(path))
            return;

        Span<byte> header = stackalloc byte[64];
        using var stream = File.OpenRead(path);
        var read = stream.Read(header);
        if (read <= 0)
            return;

        var text = System.Text.Encoding.UTF8.GetString(header[..read]).TrimStart();
        if (text.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Downloaded data is a web page, not the actual file. The link may require a browser or is not a direct download URL.");
        }
    }

    private static void FinalizeDownload(FileDownloadState state, DownloadItem item)
    {
        if (state.TotalBytes <= 0)
        {
            if (File.Exists(state.OutputPath))
                File.Delete(state.OutputPath);
            File.Move(state.PartPath, state.OutputPath);
        }
        else if (!File.Exists(state.OutputPath))
        {
            File.Move(state.PartPath, state.OutputPath);
        }
        else
        {
            File.Delete(state.PartPath);
        }

        item.OutputPath = state.OutputPath;
        item.PartFilePath = string.Empty;
        FileDownloadStateStore.Delete(state.Id);
        item.AppendDiagnostic($"Saved: {state.OutputPath}");
    }

    private static List<FileDownloadSegmentState> CreateSegments(long totalBytes, int connections)
    {
        if (totalBytes <= 0 || connections <= 1)
        {
            return
            [
                new FileDownloadSegmentState
                {
                    Start = 0,
                    End = totalBytes > 0 ? totalBytes - 1 : long.MaxValue,
                    Downloaded = 0
                }
            ];
        }

        connections = Math.Clamp(connections, 1, 16);
        var segmentSize = totalBytes / connections;
        var segments = new List<FileDownloadSegmentState>(connections);

        for (var i = 0; i < connections; i++)
        {
            var start = i * segmentSize;
            var end = i == connections - 1 ? totalBytes - 1 : start + segmentSize - 1;
            segments.Add(new FileDownloadSegmentState { Start = start, End = end, Downloaded = 0 });
        }

        return segments;
    }

    private static long GetCompletedBytes(FileDownloadState state) =>
        state.Segments.Sum(segment => segment.Downloaded);

    private static bool IsSegmentComplete(FileDownloadSegmentState segment)
    {
        if (segment.End == long.MaxValue)
            return false;

        return segment.Downloaded >= segment.End - segment.Start + 1;
    }

    private static FileDownloadProbeResult BuildProbeResult(
        string url,
        HttpResponseMessage response,
        ResolvedDirectDownload? resolved = null)
    {
        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        if (totalBytes <= 0 && response.Content.Headers.ContentRange is { } contentRange)
        {
            if (contentRange.Length.HasValue)
                totalBytes = contentRange.Length.Value;
        }
        else if (totalBytes <= 0 && response.Headers.TryGetValues("Content-Range", out var ranges))
        {
            var value = ranges.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value))
            {
                var slash = value.LastIndexOf('/');
                if (slash >= 0 && long.TryParse(value[(slash + 1)..], out var parsed))
                    totalBytes = parsed;
            }
        }

        var supportsRanges = !resolved?.ForceSingleConnection ?? true;
        if (supportsRanges)
        {
            supportsRanges = string.Equals(
                response.Headers.AcceptRanges?.FirstOrDefault(),
                "bytes",
                StringComparison.OrdinalIgnoreCase)
                || response.Content.Headers.ContentRange is not null
                || response.StatusCode == HttpStatusCode.PartialContent;
        }

        return new FileDownloadProbeResult
        {
            TotalBytes = totalBytes,
            SupportsRanges = supportsRanges,
            FileName = ResolveFileNameFromHeaders(url, response),
            ContentType = response.Content.Headers.ContentType?.MediaType,
            DownloadUrl = resolved?.DownloadUrl ?? url,
            ForceSingleConnection = resolved?.ForceSingleConnection ?? false
        };
    }

    private static string ResolveFileName(DownloadItem item, string probedName, string resolvedName)
    {
        if (!string.IsNullOrWhiteSpace(item.DesiredFileName))
            return FormatHelpers.SanitizeFileName(item.DesiredFileName.Trim());

        if (!IsGenericPageName(probedName))
            return FormatHelpers.SanitizeFileName(probedName);

        if (!string.IsNullOrWhiteSpace(resolvedName))
            return FormatHelpers.SanitizeFileName(resolvedName);

        return "download.bin";
    }

    private static bool IsGenericPageName(string name) =>
        name.Equals("view", StringComparison.OrdinalIgnoreCase)
        || name.Equals("download", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

    private static string ResolveFileNameFromHeaders(string url, HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentDisposition?.FileName is { } headerName)
        {
            var cleaned = headerName.Trim('"');
            if (!string.IsNullOrWhiteSpace(cleaned))
                return cleaned;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(name) && name != "/" && !IsGenericPageName(name))
                return Uri.UnescapeDataString(name);
        }

        return "download.bin";
    }

    private static string GetUniqueOutputPath(string folder, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".bin";

        var candidate = Path.Combine(folder, fileName);
        var index = 1;
        while (File.Exists(candidate) || File.Exists(candidate + ".part"))
        {
            candidate = Path.Combine(folder, $"{baseName} ({index}){ext}");
            index++;
        }

        return candidate;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromHours(12)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        return client;
    }
}
