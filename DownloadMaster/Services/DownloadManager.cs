using System.Collections.Concurrent;

using System.IO;

using DownloadMaster.Models;



namespace DownloadMaster.Services;



public sealed class DownloadManager

{

    private readonly YtDlpService _ytDlp = new();
    private readonly InstagramDirectDownloadService _instagramDirect = new();

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
                    playlistItemIndex: item.PlaylistItemIndex);

                if (!string.IsNullOrWhiteSpace(info.Title))
                    item.Title = info.Title;
                else if (string.IsNullOrWhiteSpace(item.Title))
                    item.Title = FormatHelpers.SanitizeFileName(Path.GetFileName(item.Url.TrimEnd('/')));

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

                    if (item.IsInstagram && InstagramDirectDownloadService.CanHandle(item.Url))
                    {
                        await _instagramDirect.DownloadAsync(item, progress, linked.Token);
                    }
                    else
                    {
                        await _ytDlp.DownloadAsync(item, _settings.Current, progress, instagram: null, linked.Token);
                    }

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

                    if (IsNonRetryableError(ex.Message))

                        break;



                    item.StatusText = $"Retry {attempt}/{attempts}...";

                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), linked.Token);

                }

            }



            item.Status = DownloadStatus.Failed;

            item.ErrorMessage = lastError?.Message ?? "Unknown error";
            item.AppendDiagnostic($"Failed: {item.ErrorMessage}");
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

            CleanupInstagramCookies(item);

            _tokens.TryRemove(item.Id, out _);

            linked.Dispose();

            _slotSemaphore.Release();

        }

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

        || message.Contains("No Instagram cookies found", StringComparison.OrdinalIgnoreCase);

}


