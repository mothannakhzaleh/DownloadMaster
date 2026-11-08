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

                if (item.Status == DownloadStatus.Completed && ComputeChecksums)
                    _ = FileChecksumService.ApplySha256Async(item);

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
