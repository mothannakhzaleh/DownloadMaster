using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Win32.SafeHandles;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

/// <summary>
/// IDM-style direct-file engine: multi-connection Range segments written into a pre-allocated
/// .part file, dynamic segment splitting (work stealing), crash-safe resume state flushed about
/// once per second, the global <see cref="SpeedLimiter"/>, and 403/410 → "link expired".
/// </summary>
public sealed class HttpFileDownloadService
{
    /// <summary>A finished connection only steals from a segment with more than this left.</summary>
    public const long MinSplitBytes = 1024 * 1024;

    private const int BufferSize = 128 * 1024;
    private const int ProgressIntervalMs = 350;
    private const int StateFlushIntervalMs = 1000;

    private static readonly HttpClient Http = CreateClient(useCookies: true);

    // Used when the browser supplies its own Cookie header, which a cookie-jar handler would override.
    private static readonly HttpClient HttpBrowserCookies = CreateClient(useCookies: false);

    // Browser headers for the download running on this async flow (flows into the segment tasks).
    private static readonly AsyncLocal<IReadOnlyDictionary<string, string>?> BrowserHeaders = new();
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

    /// <param name="defaultConnections">Used when the item does not ask for a connection count (AppSettings.FileDownloadConnections).</param>
    public async Task DownloadAsync(
        DownloadItem item,
        int defaultConnections,
        IProgress<DownloadProgressReport>? progress,
        Func<Task>? waitIfPausedAsync,
        CancellationToken ct)
    {
        item.AppendDiagnostic($"Starting HTTP download: {item.Url}");
        BrowserHeaders.Value = item.RequestHeaders;
        if (item.RequestHeaders is { Count: > 0 } browserHeaders)
            item.AppendDiagnostic($"Browser headers: {string.Join(", ", browserHeaders.Keys)}");
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
            using (var fs = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                if (totalBytes > 0)
                    fs.SetLength(totalBytes);
            }

            var supportsRanges = probe.SupportsRanges && totalBytes > 0 && !resolved.ForceSingleConnection;
            var connections = resolved.ForceSingleConnection
                ? 1
                : Math.Clamp(item.ConnectionCount > 0 ? item.ConnectionCount : defaultConnections, 1, 16);

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
            if (!string.Equals(state.Url, item.Url, StringComparison.Ordinal))
            {
                // The item was re-pointed (Refresh download address) without going through the store.
                state.Url = item.Url;
                state.DownloadUrl = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(state.DownloadUrl))
            {
                var resolved = await _resolver.ResolveAsync(state.Url, ct);
                state.DownloadUrl = resolved.DownloadUrl;
            }

            item.Title = Path.GetFileName(state.OutputPath);
            item.PartFilePath = state.PartPath;
            item.OutputPath = state.OutputPath;
            PrepareResume(state, item);
            FileDownloadStateStore.Save(state);
            item.AppendDiagnostic($"Resuming download ({GetCompletedBytes(state)}/{state.TotalBytes} bytes, {state.Segments.Count} segments)");
        }

        var engine = new SegmentEngine(state, item, waitIfPausedAsync, progress);
        await engine.RunAsync(ct);

        ValidateOutputFile(state.PartPath);
        FinalizeDownload(state, item);
        item.Segments = state.TotalBytes > 0 ? [new SegmentProgress(0, 1, 1)] : [];
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

    /// <summary>Makes saved offsets safe to continue from: a missing/short .part invalidates them.</summary>
    private static void PrepareResume(FileDownloadState state, DownloadItem item)
    {
        var partExists = File.Exists(state.PartPath);
        var partLength = partExists ? new FileInfo(state.PartPath).Length : 0;

        if (!partExists || !CoveredByLength(state, partLength))
        {
            // Saved offsets point at bytes that are no longer on disk: start over in place.
            item.AppendDiagnostic("Partial file missing or truncated — restarting from 0");
            state.Segments = CreateSegments(state.TotalBytes, state.SupportsRanges ? state.ConnectionCount : 1);
        }

        if (!state.SupportsRanges)
        {
            // Without Range support the only safe resume is a full restart.
            foreach (var segment in state.Segments)
                segment.Downloaded = 0;
        }

        using var fs = new FileStream(state.PartPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        if (state.TotalBytes > 0 && fs.Length != state.TotalBytes)
            fs.SetLength(state.TotalBytes);
    }

    private static bool CoveredByLength(FileDownloadState state, long length) =>
        state.Segments.All(s => s.Downloaded == 0 || s.Start + s.Downloaded <= length);

    /// <summary>
    /// Runs the worker pool for one download attempt. All segment bookkeeping (Start/End/Downloaded,
    /// assignment, in-flight reservations, splits) happens under <see cref="_lock"/>.
    /// </summary>
    private sealed class SegmentEngine
    {
        private readonly object _lock = new();
        private readonly FileDownloadState _state;
        private readonly DownloadItem _item;
        private readonly Func<Task>? _waitIfPausedAsync;
        private readonly IProgress<DownloadProgressReport>? _progress;
        private readonly List<SegmentRuntime> _segments;
        private readonly bool _canSplit;
        private Exception? _failure;
        private bool _dirty;
        private long _sessionBytes;
        private SafeFileHandle? _file;

        public SegmentEngine(
            FileDownloadState state,
            DownloadItem item,
            Func<Task>? waitIfPausedAsync,
            IProgress<DownloadProgressReport>? progress)
        {
            _state = state;
            _item = item;
            _waitIfPausedAsync = waitIfPausedAsync;
            _progress = progress;
            _segments = state.Segments.OrderBy(s => s.Start).Select(s => new SegmentRuntime(s)).ToList();
            _canSplit = state.SupportsRanges && state.TotalBytes > 0;
        }

        public int SplitCount { get; private set; }

        public async Task RunAsync(CancellationToken ct)
        {
            if (_segments.All(s => IsSegmentComplete(s.State)))
                return;

            using var workersCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var tickerCts = new CancellationTokenSource();
            _file = File.OpenHandle(
                _state.PartPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read,
                FileOptions.Asynchronous);

            var connections = _canSplit ? Math.Clamp(_state.ConnectionCount, 1, 16) : 1;
            var ticker = TickAsync(tickerCts.Token);

            try
            {
                var workers = new Task[connections];
                for (var i = 0; i < connections; i++)
                    workers[i] = WorkerAsync(workersCts);

                try
                {
                    await Task.WhenAll(workers);
                }
                catch
                {
                    // the real cause is recorded in _failure (siblings only see the cancellation)
                }

                ct.ThrowIfCancellationRequested();
                if (_failure is not null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(_failure);

                if (!_segments.All(s => IsSegmentComplete(s.State)))
                    throw new IOException("Download stopped before all segments finished.");
            }
            finally
            {
                tickerCts.Cancel();
                try { await ticker; } catch { /* ticker never fails the download */ }

                // Final flush: success, pause-cancel, crash-safe failure — offsets are always current.
                SaveSnapshot();
                ReportProgress();
                _file.Dispose();
                _file = null;

                if (SplitCount > 0)
                    _item.AppendDiagnostic($"Dynamic segmentation: {SplitCount} split(s), {_segments.Count} segments");
            }
        }

        private async Task WorkerAsync(CancellationTokenSource workersCts)
        {
            var ct = workersCts.Token;
            try
            {
                while (Acquire() is { } segment)
                {
                    try
                    {
                        await DownloadSegmentAsync(segment, ct);
                    }
                    finally
                    {
                        lock (_lock)
                        {
                            segment.Assigned = false;
                            segment.InFlight = 0;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // paused/cancelled by the user, or a sibling failed
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref _failure, ex, null);
                try { workersCts.Cancel(); } catch (ObjectDisposedException) { }
            }
        }

        /// <summary>Next unassigned unfinished segment, else split the busiest one (work stealing).</summary>
        private SegmentRuntime? Acquire()
        {
            lock (_lock)
            {
                foreach (var candidate in _segments)
                {
                    if (!candidate.Assigned && !IsSegmentComplete(candidate.State))
                    {
                        candidate.Assigned = true;
                        return candidate;
                    }
                }

                if (!_canSplit)
                    return null;

                SegmentRuntime? victim = null;
                long victimOffset = 0, victimRemaining = 0;
                foreach (var candidate in _segments)
                {
                    if (!candidate.Assigned || candidate.State.End == long.MaxValue)
                        continue;

                    // Bytes already being written count as taken, so the split never lands inside them.
                    var offset = candidate.State.Start + candidate.State.Downloaded + candidate.InFlight;
                    var remaining = candidate.State.End - offset + 1;
                    if (remaining > victimRemaining)
                    {
                        victim = candidate;
                        victimOffset = offset;
                        victimRemaining = remaining;
                    }
                }

                if (victim is null || victimRemaining <= MinSplitBytes)
                    return null;

                var midpoint = victimOffset + victimRemaining / 2;
                var stolen = new FileDownloadSegmentState { Start = midpoint, End = victim.State.End, Downloaded = 0 };
                // The running worker re-reads End under this lock before every write, so it stops at midpoint - 1.
                victim.State.End = midpoint - 1;

                var runtime = new SegmentRuntime(stolen) { Assigned = true };
                var index = _segments.IndexOf(victim) + 1;
                _segments.Insert(index, runtime);
                _state.Segments.Insert(Math.Min(_state.Segments.IndexOf(victim.State) + 1, _state.Segments.Count), stolen);
                SplitCount++;
                _dirty = true;
                return runtime;
            }
        }

        private async Task DownloadSegmentAsync(SegmentRuntime segment, CancellationToken ct)
        {
            var noProgressAttempts = 0;
            var buffer = new byte[BufferSize];

            while (true)
            {
                long start, end;
                lock (_lock)
                {
                    if (IsSegmentComplete(segment.State))
                        return;

                    if (!_state.SupportsRanges && segment.State.Downloaded > 0)
                    {
                        // A dropped non-resumable stream has to start over from byte 0.
                        segment.State.Downloaded = 0;
                    }

                    start = segment.State.Start + segment.State.Downloaded;
                    end = segment.State.End;
                }

                ct.ThrowIfCancellationRequested();
                if (_waitIfPausedAsync is not null)
                    await _waitIfPausedAsync();

                var downloadUrl = string.IsNullOrWhiteSpace(_state.DownloadUrl) ? _state.Url : _state.DownloadUrl;
                using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                if (_state.SupportsRanges)
                    request.Headers.Range = end == long.MaxValue ? new RangeHeaderValue(start, null) : new RangeHeaderValue(start, end);

                using var response = await SendAsync(request, ct);
                ValidateSegmentResponse(response, start);

                var before = segment.State.Downloaded;
                await using (var network = await response.Content.ReadAsStreamAsync(ct))
                {
                    await CopyRangeAsync(segment, network, buffer, ct);
                }

                lock (_lock)
                {
                    if (IsSegmentComplete(segment.State))
                        return;

                    if (segment.State.End == long.MaxValue)
                    {
                        // Unknown size: the server closing the stream is the end of the file.
                        var length = segment.State.Start + segment.State.Downloaded;
                        segment.State.End = length - 1;
                        _state.TotalBytes = length;
                        RandomAccess.SetLength(_file!, length);
                        _dirty = true;
                        return;
                    }

                    // The connection ended early: ask again for the rest of the range.
                    noProgressAttempts = segment.State.Downloaded > before ? 0 : noProgressAttempts + 1;
                }

                if (noProgressAttempts >= 3)
                    throw new IOException("The server keeps closing the connection without sending data.");
            }
        }

        private async Task CopyRangeAsync(SegmentRuntime segment, Stream network, byte[] buffer, CancellationToken ct)
        {
            var limiter = SpeedLimiter.Global;
            while (true)
            {
                var chunk = limiter.GetChunkSize(buffer.Length);
                var read = await network.ReadAsync(buffer.AsMemory(0, chunk), ct);
                if (read <= 0)
                    return;

                await limiter.WaitAsync(read, ct);
                if (_waitIfPausedAsync is not null)
                    await _waitIfPausedAsync();

                long position;
                int allowed;
                lock (_lock)
                {
                    position = segment.State.Start + segment.State.Downloaded;
                    allowed = segment.State.End == long.MaxValue
                        ? read
                        : (int)Math.Clamp(segment.State.End - position + 1, 0, read);
                    segment.InFlight = allowed;
                }

                if (allowed > 0)
                {
                    // Positional, unbuffered write: once it returns the bytes are in the OS cache,
                    // so counting them below is safe even if the process is killed right after.
                    await RandomAccess.WriteAsync(_file!, buffer.AsMemory(0, allowed), position, ct);
                    lock (_lock)
                    {
                        segment.State.Downloaded += allowed;
                        segment.InFlight = 0;
                        _sessionBytes += allowed;
                        _dirty = true;
                    }
                }

                // Range finished — or another connection took over the rest (End moved down).
                if (allowed < read)
                    return;

                lock (_lock)
                {
                    if (IsSegmentComplete(segment.State))
                        return;
                }
            }
        }

        private void ValidateSegmentResponse(HttpResponseMessage response, long start)
        {
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Gone)
                throw new DownloadLinkExpiredException(response.StatusCode);

            var total = _state.TotalBytes;
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // "bytes */<size>": the link serves a file too small for our offsets.
                if (total > 0 && response.Content.Headers.ContentRange?.Length is { } actual && actual != total)
                    throw new InvalidOperationException(DifferentFileMessage(actual, total));
                throw new InvalidOperationException($"The server refused the byte range starting at {start}.");
            }

            response.EnsureSuccessStatusCode();

            if (DirectDownloadLinkResolver.IsHtmlResponse(response))
            {
                throw new InvalidOperationException(
                    "That link returned a web page instead of a file. Use a direct download URL (ends with the file name), not a sharing page.");
            }

            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                var range = response.Content.Headers.ContentRange;
                if (range?.From is { } from && from != start)
                    throw new InvalidOperationException($"The server sent bytes from {from} instead of {start}.");
                if (total > 0 && range?.Length is { } length && length != total)
                    throw new InvalidOperationException(DifferentFileMessage(length, total));
                return;
            }

            // 200 OK: the whole file from byte 0.
            if (_state.SupportsRanges && start > 0)
                throw new InvalidOperationException("The server ignored the byte range, so this download cannot be resumed from the same link.");
            if (total > 0 && response.Content.Headers.ContentLength is { } contentLength && contentLength != total)
                throw new InvalidOperationException(DifferentFileMessage(contentLength, total));
        }

        private static string DifferentFileMessage(long actual, long expected) =>
            $"The link now points to a different file ({FormatHelpers.FormatBytes(actual)} instead of {FormatHelpers.FormatBytes(expected)}).";

        private async Task TickAsync(CancellationToken ct)
        {
            var flush = Stopwatch.StartNew();
            ReportProgress();
            try
            {
                while (true)
                {
                    await Task.Delay(ProgressIntervalMs, ct);
                    ReportProgress();
                    if (flush.ElapsedMilliseconds >= StateFlushIntervalMs)
                    {
                        flush.Restart();
                        SaveSnapshot();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // download finished or stopped
            }
        }

        private void SaveSnapshot()
        {
            FileDownloadState snapshot;
            lock (_lock)
            {
                if (!_dirty)
                    return;

                _dirty = false;
                snapshot = _state.Clone();
            }

            try
            {
                FileDownloadStateStore.Save(snapshot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lock (_lock) _dirty = true; // retry on the next tick
            }
        }

        // Speed over a sliding window of recent ticks (this session only, so resumes don't inflate it).
        private readonly Queue<(long Ticks, long Bytes)> _samples = new();

        private void ReportProgress()
        {
            long completed, total, session;
            SegmentProgress[] segments;
            lock (_lock)
            {
                completed = _segments.Sum(s => s.State.Downloaded);
                total = _state.TotalBytes;
                session = _sessionBytes;
                segments = total > 0
                    ? _segments
                        .Select(s => new SegmentProgress(
                            s.State.Start / (double)total,
                            Math.Min(s.State.End + 1, total) / (double)total,
                            s.State.Downloaded / (double)total))
                        .ToArray()
                    : [];
            }

            double speed;
            lock (_samples)
            {
                var now = Environment.TickCount64;
                _samples.Enqueue((now, session));
                while (_samples.Count > 2 && now - _samples.Peek().Ticks > 3000)
                    _samples.Dequeue();
                var (firstTicks, firstBytes) = _samples.Peek();
                speed = now > firstTicks ? (session - firstBytes) * 1000.0 / (now - firstTicks) : 0;
            }

            var percent = total > 0 ? completed * 100.0 / total : completed > 0 ? 50 : 0;
            var remaining = total > 0 ? total - completed : 0;
            var eta = speed > 0 && remaining > 0 ? TimeSpan.FromSeconds(remaining / speed) : TimeSpan.Zero;

            _item.Segments = segments;
            _progress?.Report(new DownloadProgressReport
            {
                Percent = Math.Min(99.9, percent),
                TotalText = total > 0
                    ? $"{FormatHelpers.FormatBytes(completed)} / {FormatHelpers.FormatBytes(total)}"
                    : FormatHelpers.FormatBytes(completed),
                SpeedText = speed > 0 ? FormatHelpers.FormatSpeed(speed) : string.Empty,
                EtaText = eta > TimeSpan.Zero && eta < TimeSpan.FromDays(4) ? eta.ToString(@"hh\:mm\:ss") : string.Empty
            });
        }
    }

    private sealed class SegmentRuntime(FileDownloadSegmentState state)
    {
        public FileDownloadSegmentState State { get; } = state;
        public bool Assigned { get; set; }
        public long InFlight { get; set; }
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

        connections = (int)Math.Clamp(connections, 1, Math.Min(16, totalBytes));
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
        // The probe asks for bytes 0-0, so on a 206 reply Content-Length is 1 and the real size
        // is the total after the slash in Content-Range ("bytes 0-0/10485760").
        var totalBytes = response.StatusCode == HttpStatusCode.PartialContent
            ? 0
            : response.Content.Headers.ContentLength ?? 0;
        if (response.Content.Headers.ContentRange is { Length: { } rangeTotal })
        {
            totalBytes = rangeTotal;
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

    private static Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var headers = BrowserHeaders.Value;
        if (headers is not { Count: > 0 })
            return Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        var hasCookie = false;
        foreach (var (name, value) in headers)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value) || IsRestrictedHeader(name))
                continue;

            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
            hasCookie |= name.Equals("Cookie", StringComparison.OrdinalIgnoreCase);
        }

        var client = hasCookie ? HttpBrowserCookies : Http;
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    // Headers the HTTP stack manages itself; forwarding them from the browser would break requests.
    private static bool IsRestrictedHeader(string name) =>
        name.Equals("Host", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Range", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Connection", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase);

    private static HttpClient CreateClient(bool useCookies)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            UseCookies = useCookies
        };
        if (useCookies)
            handler.CookieContainer = new CookieContainer();

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromHours(12)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        return client;
    }
}
