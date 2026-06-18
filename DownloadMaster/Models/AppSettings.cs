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
