namespace DownloadMaster.Models;

public sealed class FileDownloadState
{
    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string PartPath { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public bool SupportsRanges { get; set; }
    public int ConnectionCount { get; set; } = 8;
    public List<FileDownloadSegmentState> Segments { get; set; } = [];
}

public sealed class FileDownloadSegmentState
{
    public long Start { get; set; }
    public long End { get; set; }
    public long Downloaded { get; set; }
}

public sealed class FileDownloadProbeResult
{
    public long TotalBytes { get; init; }
    public bool SupportsRanges { get; init; }
    public string FileName { get; init; } = "download.bin";
    public string? ContentType { get; init; }
    public string DownloadUrl { get; init; } = string.Empty;
    public bool ForceSingleConnection { get; init; }
}
