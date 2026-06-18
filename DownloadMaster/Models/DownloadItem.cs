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
    private string _partFilePath = string.Empty;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Url { get; init; } = string.Empty;
    public DownloadKind Kind { get; init; } = DownloadKind.Video;
    public bool IsInstagram { get; init; }
    public InstagramBrowser InstagramBrowser { get; init; } = InstagramBrowser.Chrome;
    public string? InstagramCookieFile { get; init; }
    public string? InstagramCookieTempDir { get; init; }
    public bool NoPlaylist { get; init; }
    public int? PlaylistItemIndex { get; init; }
    public string? InstagramMediaPk { get; init; }
    public string Quality { get; init; } = "1080p";
    public string Format { get; init; } = "mp4";
    public string SaveFolder { get; init; } = string.Empty;
    public string? DesiredFileName { get; init; }
    public int ConnectionCount { get; init; } = 8;
    public string PartFilePath
    {
        get => _partFilePath;
        set
        {
            _partFilePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRetry));
        }
    }
    public string OutputPath
    {
        get => _outputPath;
        set { _outputPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanPlay)); OnPropertyChanged(nameof(CanOpenFolder)); }
    }
    public string Thumbnail { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string DiagnosticsLog { get; set; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public bool CanCopyDiagnostics =>
        Status == DownloadStatus.Failed && !string.IsNullOrWhiteSpace(DiagnosticsLog);

    public void AppendDiagnostic(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        DiagnosticsLog = string.IsNullOrWhiteSpace(DiagnosticsLog) ? line : DiagnosticsLog + Environment.NewLine + line;
        NotifyDiagnosticsChanged();
    }

    public string BuildDiagnosticReport()
    {
        var lines = new List<string>
        {
            "DownloadMaster diagnostic report",
            $"URL: {Url}",
            $"Media PK: {InstagramMediaPk ?? "(none)"}",
            $"Slide/img_index: {(PlaylistItemIndex?.ToString() ?? "(all)")}",
            $"Save folder: {SaveFolder}",
            $"Status: {Status}",
            $"Error: {ErrorMessage}",
            string.Empty,
            "Steps:",
            DiagnosticsLog
        };

        return string.Join(Environment.NewLine, lines);
    }

    public void NotifyDiagnosticsChanged() => OnPropertyChanged(nameof(CanCopyDiagnostics));

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
            OnPropertyChanged(nameof(CanResume));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CanRetry));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(CanPlay));
            OnPropertyChanged(nameof(CanOpenFolder));
            OnPropertyChanged(nameof(CanCopyDiagnostics));
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

    public bool CanPause => Kind == DownloadKind.DirectFile && Status == DownloadStatus.Downloading;
    public bool CanResume => Kind == DownloadKind.DirectFile && Status == DownloadStatus.Paused;
    public bool CanCancel => Status is DownloadStatus.Queued or DownloadStatus.Fetching or DownloadStatus.Downloading or DownloadStatus.Paused;
    public bool CanRetry => Status == DownloadStatus.Failed
        || (Status == DownloadStatus.Cancelled && Kind == DownloadKind.DirectFile
            && !string.IsNullOrWhiteSpace(PartFilePath) && File.Exists(PartFilePath));
    public bool IsCompleted => Status == DownloadStatus.Completed;
    public bool CanPlay => IsCompleted && !string.IsNullOrWhiteSpace(OutputPath) && File.Exists(OutputPath);
    public bool CanOpenFolder => IsCompleted && (!string.IsNullOrWhiteSpace(OutputPath) || !string.IsNullOrWhiteSpace(SaveFolder));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
