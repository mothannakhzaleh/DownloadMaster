namespace DownloadMaster.Services;

/// <summary>Throttled progress snapshot reported by every downloader (yt-dlp, Instagram, HTTP file).</summary>
public readonly record struct DownloadProgressReport
{
    public double Percent { get; init; }
    public string TotalText { get; init; }
    public string SpeedText { get; init; }
    public string EtaText { get; init; }
}
