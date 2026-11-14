using System.Collections.Concurrent;
using System.IO;
using DownloadMaster.Models;
using DownloadMaster.Services.Localization;

namespace DownloadMaster.Services;

public sealed class DownloadManager
{
    private readonly YtDlpService _ytDlp = new();
    private readonly InstagramDirectDownloadService _instagramDirect = new();
    private readonly HttpFileDownloadService _httpFile = new();
    private readonly DownloadPauseRegistry _pauseRegistry = new();

    private readonly SettingsService _settings;

    private readonly SemaphoreSlim _slotSemaphore;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tokens = new();

    // Every item handed to this manager (for "Refresh download address" matching) and its running task.
    private readonly ConcurrentDictionary<string, DownloadItem> _tracked = new();
    private readonly ConcurrentDictionary<string, Task> _running = new();

    public DownloadManager(SettingsService settings)
    {
        _settings = settings;
        _slotSemaphore = new SemaphoreSlim(Math.Clamp(settings.Current.MaxConcurrentDownloads, 1, 5));
        SetSpeedLimit(settings.Current.SpeedLimitKbps);
    }

    /// <summary>
    /// Translates engine messages (EngineStrings keys). Set to LocalizationService.Get so they follow
    /// the UI language; defaults to English.
    /// </summary>
    public Func<string, string> Localize { get; set; } = key => EngineStrings.English.GetValueOrDefault(key, key);

    /// <summary>Hash finished direct-file downloads (SHA-256) in the background.</summary>
    public bool ComputeChecksums { get; set; } = true;

    /// <summary>Current global limit in KB/s (0 = unlimited).</summary>
    public int SpeedLimitKbps => (int)(SpeedLimiter.Global.BytesPerSecond / 1024);

    /// <summary>
    /// Global speed limit in KB/s for every direct-file connection, applied immediately to running
    /// downloads (0 = unlimited). yt-dlp downloads pick up AppSettings.SpeedLimitKbps when they start.
    /// </summary>
    public void SetSpeedLimit(int kbps) => SpeedLimiter.Global.SetLimitKbps(Math.Max(0, kbps));

    public void UpdateConcurrency(int max)
    {
        // Recreate on settings save from UI
    }

    public async Task EnqueueAsync(DownloadItem item, CancellationToken ct = default)
    {
        _tracked[item.Id] = item;
        var task = ProcessItemAsync(item, ct);
        _running[item.Id] = task;
        _ = task.ContinueWith(
            t => _running.TryRemove(new KeyValuePair<string, Task>(item.Id, t)),
            TaskScheduler.Default);
        await Task.CompletedTask;
    }

    /// <summary>Forget an item removed from the list, so it is never offered for an address refresh.</summary>
    public void Untrack(DownloadItem item) => _tracked.TryRemove(item.Id, out _);

    /// <summary>
    /// The stopped (failed/cancelled) direct-file download that a newly caught browser download
    /// can continue: same file name (ignoring a " (n)" suffix) and, when known, same size in bytes.
    /// Expired-link items win; otherwise the newest. Null when nothing matches.
    /// </summary>
    public DownloadItem? FindRefreshCandidate(string fileName, long size) =>
        _tracked.Values
            .Where(item => DownloadAddressRefresh.IsCandidate(item, fileName, size))
            .OrderByDescending(item => item.IsLinkExpired)
            .ThenByDescending(item => item.CreatedAt)
            .FirstOrDefault();

    /// <summary>
    /// "Refresh download address": point <paramref name="item"/> at <paramref name="newUrl"/> with the
    /// browser's new headers (cookies/referer/user-agent) and resume into the existing .part file.
    /// A running attempt is stopped first.
    /// </summary>
    public async Task RefreshAddressAsync(
        DownloadItem item,
        string newUrl,
        IReadOnlyDictionary<string, string>? headers)
    {
        DirectDownloadLinkResolver.EnsureSupported(newUrl);

        if (_running.TryGetValue(item.Id, out var running) && !running.IsCompleted)
        {
            if (!_tokens.TryGetValue(item.Id, out var cts))
            {
                // Still waiting for a download slot: it will start with the new address.
                DownloadAddressRefresh.Apply(item, newUrl, headers);
                return;
            }

            cts.Cancel();
            try { await running; } catch { /* the old attempt's outcome no longer matters */ }
        }

        DownloadAddressRefresh.Apply(item, newUrl, headers);
        item.Progress = 0;
        item.SpeedText = string.Empty;
        item.EtaText = string.Empty;
        item.Status = DownloadStatus.Queued;
        item.StatusText = Localize("EngineAddressRefreshed");
        await EnqueueAsync(item);
    }

    public void Cancel(DownloadItem item)
    {
        if (_tokens.TryRemove(item.Id, out var cts))
        {
            cts.Cancel();
            item.Status = DownloadStatus.Cancelled;
            item.StatusText = "Cancelled";
        }
        else if (item.Status is DownloadStatus.Queued or DownloadStatus.Paused)
        {
            item.Status = DownloadStatus.Cancelled;
            item.StatusText = "Cancelled";
        }
    }

    public void Pause(DownloadItem item)
    {
        if (item.Kind != DownloadKind.DirectFile || item.Status != DownloadStatus.Downloading)
            return;

        _pauseRegistry.Pause(item.Id);
        item.Status = DownloadStatus.Paused;
        item.StatusText = "Paused";
    }

    public void Resume(DownloadItem item)
    {
        if (item.Kind != DownloadKind.DirectFile)
            return;

        if (item.Status == DownloadStatus.Paused)
        {
            _pauseRegistry.Resume(item.Id);
            item.Status = DownloadStatus.Downloading;
            item.StatusText = "Downloading...";
            return;
        }

        if (item.Status is DownloadStatus.Failed or DownloadStatus.Cancelled)
            _ = EnqueueAsync(item);
    }

    private async Task ProcessItemAsync(DownloadItem item, CancellationToken outerCt)
    {
        await _slotSemaphore.WaitAsync(outerCt);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        _tokens[item.Id] = linked;
        _pauseRegistry.Register(item.Id);

        try
        {
            var progress = new Progress<DownloadProgressReport>(r =>
            {
                item.Progress = r.Percent;
                item.SpeedText = r.SpeedText;
                item.EtaText = r.EtaText;
                item.SizeText = r.TotalText;
            });

            if (item.Kind == DownloadKind.DirectFile)
            {
                item.IsLinkExpired = false;
                item.Status = DownloadStatus.Fetching;
                item.StatusText = "Probing file...";
                item.Status = DownloadStatus.Downloading;
                item.StatusText = "Downloading...";
                await RunWithRetryAsync(item, async () =>
                    await _httpFile.DownloadAsync(
                        item,
                        _settings.Current.FileDownloadConnections,
                        progress,
                        () => _pauseRegistry.WaitIfPausedAsync(item.Id, linked.Token),
                        linked.Token));

                if (item.Status == DownloadStatus.Completed)
                    _ = Task.Run(() => PostProcessFileAsync(item));

                return;
            }

            item.Status = DownloadStatus.Fetching;
            item.StatusText = "Fetching info...";

            if (item.IsInstagram && InstagramDirectDownloadService.CanHandle(item.Url))
            {
                if (string.IsNullOrWhiteSpace(item.Title))
                    item.Title = FormatHelpers.SanitizeFileName(Path.GetFileName(item.Url.TrimEnd('/')));
            }
            else if (string.IsNullOrWhiteSpace(item.Title) || item.IsInstagram)
            {
                var info = await _ytDlp.FetchInfoAsync(
                    item.Url,
                    item.IsInstagram ? "jpg" : _settings.Current.PreferredFormat,
                    instagram: null,
                    item.NoPlaylist,
                    linked.Token,
                    cookieFile: item.InstagramCookieFile,
                    playlistItemIndex: item.PlaylistItemIndex,
                    requestHeaders: item.RequestHeaders);

                if (!string.IsNullOrWhiteSpace(info.Title))
                    item.Title = info.Title;
                else if (string.IsNullOrWhiteSpace(item.Title))
                    item.Title = FormatHelpers.SanitizeFileName(Path.GetFileName(item.Url.TrimEnd('/')));

                item.Thumbnail = info.Thumbnail;
            }

            item.Status = DownloadStatus.Downloading;
            item.StatusText = "Downloading...";

            await RunWithRetryAsync(item, async () =>
            {
                if (item.IsInstagram && InstagramDirectDownloadService.CanHandle(item.Url))
                    await _instagramDirect.DownloadAsync(item, progress, linked.Token);
                else
                    await _ytDlp.DownloadAsync(item, _settings.Current, progress, instagram: null, linked.Token);
            });

            // Video / Instagram downloads get the antivirus scan too (archive extraction is for files only).
            if (item.Status == DownloadStatus.Completed && _settings.Current.ScanDownloadsWithAntivirus
                && !string.IsNullOrWhiteSpace(item.OutputPath) && File.Exists(item.OutputPath))
                _ = Task.Run(() => ScanQuietlyAsync(item, item.OutputPath));
        }
        catch (OperationCanceledException)
        {
            if (item.Status != DownloadStatus.Paused)
            {
                item.Status = DownloadStatus.Cancelled;
                item.StatusText = "Cancelled";
            }
        }
        catch (Exception ex)
        {
            item.Status = DownloadStatus.Failed;
            item.ErrorMessage = ex.Message;
            item.AppendDiagnostic($"Failed: {ex.Message}");
            item.StatusText = "Failed";
        }
        finally
        {
            CleanupInstagramCookies(item);
            _pauseRegistry.Unregister(item.Id);
            _tokens.TryRemove(new KeyValuePair<string, CancellationTokenSource>(item.Id, linked));
            linked.Dispose();
            _slotSemaphore.Release();
        }
    }

    private async Task RunWithRetryAsync(DownloadItem item, Func<Task> action)
    {
        var attempts = Math.Max(1, _settings.Current.AutoRetryAttempts);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await action();
                item.Status = DownloadStatus.Completed;
                item.Progress = 100;
                item.SpeedText = string.Empty;
                item.EtaText = string.Empty;
                item.StatusText = "Completed";
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DownloadLinkExpiredException ex)
            {
                // Retrying the same dead link is pointless: keep .part + state and wait for a refresh.
                item.IsLinkExpired = true;
                item.Status = DownloadStatus.Failed;
                item.ErrorMessage = Localize("EngineLinkExpired");
                item.AppendDiagnostic($"Failed: {ex.Message}");
                item.StatusText = Localize("EngineLinkExpiredStatus");
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (IsNonRetryableError(ex.Message))
                    break;

                item.StatusText = $"Retry {attempt}/{attempts}...";
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
            }
        }

        item.Status = DownloadStatus.Failed;
        item.ErrorMessage = lastError?.Message ?? "Unknown error";
        item.AppendDiagnostic($"Failed: {item.ErrorMessage}");
        item.StatusText = "Failed";
    }

    /// <summary>
    /// After a direct-file download completes, in the background and in this order (each step needs
    /// the file to itself): SHA-256 → antivirus scan → extract archive (→ delete it). A threat skips
    /// extraction. Results go to the item's ScanNote / ArchiveNote; nothing here fails the download.
    /// </summary>
    /// <summary>WebP → PNG converter (input, output); set by the app, which has FFmpeg/ImageMagick.</summary>
    public static Func<string, string, CancellationToken, Task>? ConvertImageToPng { get; set; }

    private async Task PostProcessFileAsync(DownloadItem item)
    {
        try
        {
            if (item.ConvertWebpToPng)
                await ConvertWebpToPngAsync(item);

            if (ComputeChecksums)
                await FileChecksumService.ApplySha256Async(item);

            var settings = _settings.Current;
            var path = item.OutputPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            var threat = false;
            if (settings.ScanDownloadsWithAntivirus)
                threat = await ScanAsync(item, path);

            if (settings.ExtractArchivesWhenDone && !threat)
                await ExtractArchiveAsync(item, path, settings.DeleteArchiveAfterExtract);
        }
        catch (Exception ex)
        {
            item.AppendDiagnostic($"After-download step failed: {ex.Message}");
        }
    }

    private static async Task ConvertWebpToPngAsync(DownloadItem item)
    {
        var path = item.OutputPath;
        var convert = ConvertImageToPng;
        if (convert is null || string.IsNullOrWhiteSpace(path) || !File.Exists(path)
            || !string.Equals(Path.GetExtension(path), ".webp", StringComparison.OrdinalIgnoreCase))
            return;

        var png = Path.ChangeExtension(path, ".png");
        for (var n = 2; File.Exists(png); n++)
            png = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)} ({n}).png");

        try
        {
            await convert(path, png, CancellationToken.None);
            if (!File.Exists(png) || new FileInfo(png).Length == 0)
                throw new IOException("no PNG was written");

            File.Delete(path);
            item.OutputPath = png;
            item.AppendDiagnostic($"Saved as PNG: {png}");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(png)) File.Delete(png); } catch { }
            item.AppendDiagnostic($"Kept the WebP image, PNG conversion failed: {ex.Message}");
        }
    }

    private static async Task ScanQuietlyAsync(DownloadItem item, string path)
    {
        try
        {
            await ScanAsync(item, path);
        }
        catch (Exception ex)
        {
            item.AppendDiagnostic($"After-download scan failed: {ex.Message}");
        }
    }

    /// <summary>Returns true when a threat was found.</summary>
    private static async Task<bool> ScanAsync(DownloadItem item, string path)
    {
        item.ScanNote = new StatusNote("ScanRunning");
        var result = await AntivirusScanner.ScanAsync(path);
        switch (result.Outcome)
        {
            case ScanOutcome.Clean:
                item.ScanNote = new StatusNote("ScanClean", result.Scanner);
                item.AppendDiagnostic($"{result.Scanner}: no threats found");
                return false;

            case ScanOutcome.ThreatFound:
                var threat = string.IsNullOrWhiteSpace(result.Detail) ? result.Scanner : $"{result.Detail}, {result.Scanner}";
                item.ScanNote = new StatusNote("ScanThreat", threat);
                item.AppendDiagnostic($"{result.Scanner}: THREAT FOUND {result.Detail} — the file was kept: {path}");
                return true;

            case ScanOutcome.NoScanner:
                item.ScanNote = new StatusNote(OperatingSystem.IsWindows() ? "ScanNoDefender" : "ScanNoClamAv");
                item.AppendDiagnostic("Antivirus scan skipped: no scanner found");
                return false;

            default:
                item.ScanNote = new StatusNote("ScanFailed", result.Detail);
                item.AppendDiagnostic($"{result.Scanner}: scan failed: {result.Detail}");
                return false;
        }
    }

    private async Task ExtractArchiveAsync(DownloadItem item, string path, bool deleteArchive)
    {
        var info = ArchiveExtractor.Identify(path);
        if (info is null)
            return;

        // Notes belong on the first part's row when this is a later part of a set.
        var noteItem = info.IsFirstPart ? item : FindCompletedItem(info.FirstPartPath) ?? item;
        var result = await ArchiveExtractor.ExtractAsync(
            path,
            deleteArchive,
            PendingDownloadPaths(item),
            onStarted: _ => noteItem.ArchiveNote = new StatusNote("ArchiveExtracting"));

        switch (result.Outcome)
        {
            case ArchiveExtractOutcome.Extracted:
                noteItem.ArchiveNote = new StatusNote("ArchiveExtracted", Path.GetFileName(result.TargetFolder));
                noteItem.AppendDiagnostic($"Extracted to {result.TargetFolder}");
                if (result.DeletedFiles is { Count: > 0 } deleted)
                    noteItem.AppendDiagnostic($"Deleted the archive: {string.Join(", ", deleted.Select(Path.GetFileName))}");
                break;

            case ArchiveExtractOutcome.WaitingForParts:
                noteItem.ArchiveNote = new StatusNote("ArchiveWaitingForParts");
                break;

            case ArchiveExtractOutcome.NeedsSevenZip:
                noteItem.ArchiveNote = new StatusNote("ArchiveNeeds7Zip");
                noteItem.AppendDiagnostic("Extraction skipped: 7-Zip (7z / 7zz / 7za) was not found");
                break;

            case ArchiveExtractOutcome.Failed:
                noteItem.ArchiveNote = new StatusNote("ArchiveExtractFailed", result.Detail);
                noteItem.AppendDiagnostic($"Extraction failed: {result.Detail}");
                break;
        }
    }

    private DownloadItem? FindCompletedItem(string outputPath) =>
        _tracked.Values.FirstOrDefault(i =>
            i.Status == DownloadStatus.Completed
            && !string.IsNullOrWhiteSpace(i.OutputPath)
            && string.Equals(Path.GetFullPath(i.OutputPath), outputPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Where unfinished direct-file downloads will land, so a multi-part set waits for all of them.</summary>
    private List<string> PendingDownloadPaths(DownloadItem except)
    {
        var paths = new List<string>();
        foreach (var other in _tracked.Values)
        {
            if (ReferenceEquals(other, except) || other.Kind != DownloadKind.DirectFile
                || other.Status is not (DownloadStatus.Queued or DownloadStatus.Fetching or DownloadStatus.Downloading or DownloadStatus.Paused))
                continue;

            try
            {
                if (!string.IsNullOrWhiteSpace(other.OutputPath))
                {
                    paths.Add(other.OutputPath);
                    continue;
                }

                var name = other.DesiredFileName;
                if (string.IsNullOrWhiteSpace(name) && Uri.TryCreate(other.Url, UriKind.Absolute, out var uri))
                    name = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(other.SaveFolder))
                    paths.Add(Path.Combine(other.SaveFolder, Path.GetFileName(name)));
            }
            catch (ArgumentException)
            {
                // odd name: ignore
            }
        }

        return paths;
    }

    private static void CleanupInstagramCookies(DownloadItem item)
    {
        if (string.IsNullOrWhiteSpace(item.InstagramCookieTempDir))
            return;

        try
        {
            if (Directory.Exists(item.InstagramCookieTempDir))
                Directory.Delete(item.InstagramCookieTempDir, true);
        }
        catch
        {
            // best effort cleanup
        }
    }

    private static bool IsNonRetryableError(string message) =>
        message.Contains("Could not copy", StringComparison.OrdinalIgnoreCase)
        || message.Contains("cookie database", StringComparison.OrdinalIgnoreCase)
        || message.Contains("login required", StringComparison.OrdinalIgnoreCase)
        || message.Contains("csrf token", StringComparison.OrdinalIgnoreCase)
        || message.Contains("registered users who follow", StringComparison.OrdinalIgnoreCase)
        || message.Contains("No Instagram cookies found", StringComparison.OrdinalIgnoreCase)
        || message.Contains("different file", StringComparison.OrdinalIgnoreCase)
        || message.Contains("ignored the byte range", StringComparison.OrdinalIgnoreCase);
}
