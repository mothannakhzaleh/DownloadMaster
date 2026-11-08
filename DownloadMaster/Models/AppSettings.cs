namespace DownloadMaster.Models;

using System.ComponentModel;
using System.Runtime.CompilerServices;

public sealed class AppSettings
{
    public string DefaultSavePath { get; set; } = string.Empty;
    public int MaxConcurrentDownloads { get; set; } = 3;
    public int SpeedLimitKbps { get; set; } = 0;
    public int AutoRetryAttempts { get; set; } = 3;
    public string DefaultQuality { get; set; } = "1080p";
    public string PreferredFormat { get; set; } = "mp4";
    public string NamingTemplate { get; set; } = "{title}_{quality}.{ext}";
    public bool DownloadSubtitles { get; set; }
    public bool ClipboardMonitor { get; set; } = true;
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public AppLanguage Language { get; set; } = AppLanguage.English;
    public InstagramBrowser InstagramBrowser { get; set; } = InstagramBrowser.Chrome;
    public string InstagramCookiesPath { get; set; } = string.Empty;
    public int FileDownloadConnections { get; set; } = 8;
    /// <summary>Closing the window hides it to the system tray / menu bar instead of exiting.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Let the browser extension hand browser downloads to DownloadMaster.</summary>
    public bool InterceptBrowserDownloads { get; set; } = true;

    /// <summary>File extensions the extension always hands over (others only when large).</summary>
    public List<string> InterceptFileTypes { get; set; } =
    [
        "zip", "7z", "rar", "tar", "gz", "xz", "bz2",
        "exe", "msi", "dmg", "pkg", "deb", "rpm", "appimage", "apk", "iso", "img",
        "mp4", "mkv", "avi", "mov", "webm", "wmv", "flv",
        "mp3", "flac", "wav", "m4a", "aac", "ogg",
        "pdf", "epub"
    ];

    /// <summary>Sort downloads into Video/Music/Programs/Compressed/Documents/Images/Other sub-folders of the save folder.</summary>
    public bool SortIntoCategoryFolders { get; set; }

    /// <summary>Queue scheduler: hold new downloads until this local time (HH:mm) each day.</summary>
    public bool SchedulerStartEnabled { get; set; }
    public string SchedulerStartTime { get; set; } = "02:00";

    /// <summary>Queue scheduler: pause running file downloads at this local time (HH:mm) each day.</summary>
    public bool SchedulerStopEnabled { get; set; }
    public string SchedulerStopTime { get; set; } = "07:00";
}

public sealed class VideoInfo
{
    public string Title { get; set; } = string.Empty;
    public string Thumbnail { get; set; } = string.Empty;
    public int Duration { get; set; }
    public string Uploader { get; set; } = string.Empty;
    public bool IsPlaylist { get; set; }
    public int PlaylistCount { get; set; }
    public List<VideoInfoEntry> Entries { get; set; } = [];
    public int MaxHeight { get; set; }
    public string RecommendedQuality { get; set; } = "1080p";
    public List<string> AvailableQualities { get; set; } = [];
    public string RecommendedFormat { get; set; } = "mp4";
    public List<string> AvailableFormats { get; set; } = [];
}

public sealed class VideoInfoEntry : INotifyPropertyChanged
{
    private bool _selected = true;

    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    public bool Selected
    {
        get => _selected;
        set { _selected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
