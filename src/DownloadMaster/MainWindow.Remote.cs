using Avalonia.Threading;
using DownloadMaster.Models;
using DownloadMaster.Services;
using DownloadMaster.Services.Queue;
using DownloadMaster.Services.Remote;

namespace DownloadMaster;

/// <summary>
/// The desktop window as the remote-control host: the phone page and the command-line tool see and
/// drive the same queue as the window. Requests arrive on socket threads and run on the UI thread.
/// </summary>
public partial class MainWindow : IRemoteHost
{
    // Called from socket threads; GetSaveFolder reads a text box, which only the UI thread may touch.
    string IRemoteHost.SaveFolder =>
        Dispatcher.UIThread.CheckAccess() ? GetSaveFolder() : Dispatcher.UIThread.Invoke(GetSaveFolder);

    /// <summary>The app's settings (App starts remote access from them).</summary>
    internal SettingsService Settings => _settings;

    Task<IReadOnlyList<RemoteDownload>> IRemoteHost.ListAsync() =>
        Dispatcher.UIThread.InvokeAsync<IReadOnlyList<RemoteDownload>>(() => _items.Select(RemoteDownload.From).ToList()).GetTask();

    Task<RemoteDownload> IRemoteHost.AddAsync(string url, string folder, string? fileName, bool asVideo) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // Category sorting applies as for the window's own downloads.
            var sort = _settings.Current.SortIntoCategoryFolders;
            var target = asVideo
                ? DownloadCategoryRouter.ResolveMediaFolder(folder, _settings.Current.PreferredFormat, sort)
                : DownloadCategoryRouter.ResolveFileFolder(folder, fileName ?? url, null, sort);
            var item = ManagerRemoteHost.CreateItem(url, target, fileName, asVideo, _settings.Current);

            _items.Insert(0, item);
            AttachItemHandlers(item);
            UpdateQueueEmptyState();
            await StartOrScheduleAsync(item);
            return RemoteDownload.From(item);
        });

    Task<bool> IRemoteHost.ActAsync(string id, string action) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item is null)
                return false;

            switch (action)
            {
                case "pause":
                    _downloads.Pause(item);
                    break;
                case "resume" when item.CanRetry:
                    item.ErrorMessage = string.Empty;
                    item.Progress = 0;
                    item.Status = DownloadStatus.Queued;
                    item.StatusText = _loc.Get("StatusQueued");
                    await StartOrScheduleAsync(item);
                    break;
                case "resume":
                    _downloads.Resume(item);
                    RefreshItemStatusText(item);
                    break;
                case "cancel":
                    _downloads.Cancel(item);
                    _scheduler.Forget(item);
                    break;
                case "remove":
                    _downloads.Cancel(item);
                    _downloads.Untrack(item);
                    _scheduler.Forget(item);
                    _items.Remove(item);
                    UpdateQueueEmptyState();
                    break;
                default:
                    return false;
            }

            return true;
        });
}
