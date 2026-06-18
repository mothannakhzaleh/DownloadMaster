using System.Collections.Concurrent;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed class DownloadManager
{
    private readonly YtDlpService _ytDlp = new();
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _slotSemaphore;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tokens = new();

    public DownloadManager(SettingsService settings)
    {
        _settings = settings;
        _slotSemaphore = new SemaphoreSlim(Math.Clamp(settings.Current.MaxConcurrentDownloads, 1, 5));
    }

    public void UpdateConcurrency(int max)
    {
        // Recreate on settings save from UI
    }

    public async Task EnqueueAsync(DownloadItem item, CancellationToken ct = default)
    {
        _ = ProcessItemAsync(item, ct);
        await Task.CompletedTask;
    }

    public void Cancel(DownloadItem item)
    {
        if (_tokens.TryRemove(item.Id, out var cts))
        {
            cts.Cancel();
            item.Status = DownloadStatus.Cancelled;
            item.StatusText = "Cancelled";
        }
    }

    private async Task ProcessItemAsync(DownloadItem item, CancellationToken outerCt)
    {
        await _slotSemaphore.WaitAsync(outerCt);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        _tokens[item.Id] = linked;

        try
        {
            item.Status = DownloadStatus.Fetching;
            item.StatusText = "Fetching info...";

            if (string.IsNullOrWhiteSpace(item.Title))
            {
                var info = await _ytDlp.FetchInfoAsync(item.Url, _settings.Current.PreferredFormat, linked.Token);
                item.Title = info.Title;
                item.Thumbnail = info.Thumbnail;
            }

            item.Status = DownloadStatus.Downloading;
            item.StatusText = "Downloading...";

            var progress = new Progress<DownloadProgressReport>(r =>
            {
                item.Progress = r.Percent;
                item.SpeedText = r.SpeedText;
                item.EtaText = r.EtaText;
                item.SizeText = r.TotalText;
            });

            var attempts = Math.Max(1, _settings.Current.AutoRetryAttempts);
            Exception? lastError = null;

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    await _ytDlp.DownloadAsync(item, _settings.Current, progress, linked.Token);
                    item.Status = DownloadStatus.Completed;
                    item.Progress = 100;
                    item.SpeedText = string.Empty;
                    item.EtaText = string.Empty;
                    item.StatusText = "Completed";
                    return;
                }
                catch (OperationCanceledException)
                {
                    if (linked.IsCancellationRequested)
                    {
                        item.Status = DownloadStatus.Cancelled;
                        item.StatusText = "Cancelled";
                        return;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    item.StatusText = $"Retry {attempt}/{attempts}...";
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), linked.Token);
                }
            }

            item.Status = DownloadStatus.Failed;
            item.ErrorMessage = lastError?.Message ?? "Unknown error";
            item.StatusText = "Failed";
        }
        catch (OperationCanceledException)
        {
            item.Status = DownloadStatus.Cancelled;
            item.StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            item.Status = DownloadStatus.Failed;
            item.ErrorMessage = ex.Message;
            item.StatusText = "Failed";
        }
        finally
        {
            _tokens.TryRemove(item.Id, out _);
            linked.Dispose();
            _slotSemaphore.Release();
        }
    }
}
