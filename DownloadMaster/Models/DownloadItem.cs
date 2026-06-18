using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace DownloadMaster.Models;

public sealed class DownloadItem : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private DownloadStatus _status = DownloadStatus.Queued;
    private double _progress;
    private string _statusText = string.Empty;
    private string _speedText = string.Empty;
    private string _sizeText = string.Empty;
    private string _etaText = string.Empty;
    private string _outputPath = string.Empty;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Url { get; init; } = string.Empty;
    public bool NoPlaylist { get; init; }
    public string Quality { get; init; } = "1080p";
    public string Format { get; init; } = "mp4";
    public string SaveFolder { get; init; } = string.Empty;
    public string OutputPath
    {
        get => _outputPath;
        set { _outputPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanPlay)); OnPropertyChanged(nameof(CanOpenFolder)); }
    }
    public string Thumbnail { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public DownloadStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanPause));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CanRetry));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(CanPlay));
            OnPropertyChanged(nameof(CanOpenFolder));
        }
    }

    public double Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
    }

    public string ProgressText => $"{Progress:0.#}%";

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public string SpeedText
    {
        get => _speedText;
        set { _speedText = value; OnPropertyChanged(); }
    }

    public string SizeText
    {
        get => _sizeText;
        set { _sizeText = value; OnPropertyChanged(); }
    }

    public string EtaText
    {
        get => _etaText;
        set { _etaText = value; OnPropertyChanged(); }
    }

    public bool CanPause => Status == DownloadStatus.Downloading;
    public bool CanCancel => Status is DownloadStatus.Queued or DownloadStatus.Fetching or DownloadStatus.Downloading or DownloadStatus.Paused;
    public bool CanRetry => Status == DownloadStatus.Failed;
    public bool IsCompleted => Status == DownloadStatus.Completed;
    public bool CanPlay => IsCompleted && !string.IsNullOrWhiteSpace(OutputPath) && File.Exists(OutputPath);
    public bool CanOpenFolder => IsCompleted && (!string.IsNullOrWhiteSpace(OutputPath) || !string.IsNullOrWhiteSpace(SaveFolder));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
