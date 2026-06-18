namespace DownloadMaster.Models;

public enum DownloadKind
{
    Video,
    Instagram,
    DirectFile
}

public enum DownloadStatus
{
    Queued,
    Fetching,
    Downloading,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public enum AppTheme
{
    Dark,
    Light
}

public enum AppLanguage
{
    English,
    Arabic,
    Spanish,
    French,
    German,
    Chinese,
    Swedish,
    Norwegian
}

public enum InstagramBrowser
{
    Chrome,
    Edge
}
