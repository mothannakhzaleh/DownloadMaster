using System.IO;
using DownloadMaster.Models;

namespace DownloadMaster.Services.Remote;

/// <summary>
/// The download list of the headless server: a DownloadManager plus the items it was given, with no
/// UI. The desktop app has its own host (MainWindow) because its list lives on the UI thread.
/// </summary>
public sealed class ManagerRemoteHost : IRemoteHost
{
    private readonly DownloadManager _manager;
    private readonly SettingsService _settings;
    private readonly List<DownloadItem> _items = [];
    private readonly Lock _gate = new();

    public ManagerRemoteHost(DownloadManager manager, SettingsService settings, string saveFolder)
    {
        _manager = manager;
        _settings = settings;
        SaveFolder = Path.GetFullPath(saveFolder);
    }

    public string SaveFolder { get; }

    /// <summary>Raised when an item is added (the console prints it).</summary>
    public event Action<DownloadItem>? ItemAdded;

    public IReadOnlyList<DownloadItem> Items
    {
        get { lock (_gate) return [.. _items]; }
    }

    public Task<IReadOnlyList<RemoteDownload>> ListAsync()
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<RemoteDownload>>(_items.Select(RemoteDownload.From).ToList());
    }

    public async Task<RemoteDownload> AddAsync(string url, string folder, string? fileName, bool asVideo)
    {
        var item = CreateItem(url, folder, fileName, asVideo, _settings.Current);
        await AddItemAsync(item);
        return RemoteDownload.From(item);
    }

    /// <summary>Queue a ready item (e.g. one caught by the browser extension, with its cookies).</summary>
    public async Task AddItemAsync(DownloadItem item)
    {
        lock (_gate)
            _items.Insert(0, item);
        ItemAdded?.Invoke(item);
        await _manager.EnqueueAsync(item);
    }

    public Task<bool> ActAsync(string id, string action)
    {
        DownloadItem? item;
        lock (_gate)
            item = _items.FirstOrDefault(i => i.Id == id);
        if (item is null)
            return Task.FromResult(false);

        switch (action)
        {
            case "pause":
                _manager.Pause(item);
                break;
            case "resume":
                if (item.Kind != DownloadKind.DirectFile && item.Status is DownloadStatus.Failed or DownloadStatus.Cancelled)
                    _ = _manager.EnqueueAsync(item);
                else
                    _manager.Resume(item);
                break;
            case "cancel":
                _manager.Cancel(item);
                break;
            case "remove":
                if (item.CanCancel)
                    _manager.Cancel(item);
                _manager.Untrack(item);
                lock (_gate)
                    _items.Remove(item);
                break;
            default:
                return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <summary>A queue item for a plain file (multi-connection engine) or a video page (yt-dlp).</summary>
    public static DownloadItem CreateItem(string url, string folder, string? fileName, bool asVideo, AppSettings settings)
    {
        Directory.CreateDirectory(folder);
        if (asVideo)
        {
            return new DownloadItem
            {
                Url = url,
                Title = fileName ?? string.Empty,
                Quality = settings.DefaultQuality,
                Format = settings.PreferredFormat,
                SaveFolder = folder,
                NoPlaylist = true
            };
        }

        DirectDownloadLinkResolver.EnsureSupported(url);
        return new DownloadItem
        {
            Url = url,
            Kind = DownloadKind.DirectFile,
            Title = fileName ?? url,
            DesiredFileName = fileName,
            ConnectionCount = settings.FileDownloadConnections,
            SaveFolder = folder
        };
    }
}
