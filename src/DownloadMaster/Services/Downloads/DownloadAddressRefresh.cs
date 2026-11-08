using System.IO;
using System.Net;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

/// <summary>
/// The server rejected a resume/segment request with 403 Forbidden or 410 Gone: the signed link
/// (or its cookies) expired. The .part file and resume state are kept so the download can continue
/// once the user catches the same file again in the browser ("Refresh download address").
/// </summary>
public sealed class DownloadLinkExpiredException : Exception
{
    public DownloadLinkExpiredException(HttpStatusCode statusCode)
        : base($"The download link expired (HTTP {(int)statusCode}). Download the file again in the browser to refresh the address; downloaded data is kept.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

/// <summary>Matching and re-pointing of an interrupted direct-file download to a fresh URL.</summary>
public static class DownloadAddressRefresh
{
    /// <summary>
    /// True when <paramref name="item"/> is a stopped direct-file download whose saved resume state
    /// describes the same file (name and, when known, size) as the one just caught in the browser.
    /// </summary>
    public static bool IsCandidate(DownloadItem item, string fileName, long size)
    {
        if (item.Kind != DownloadKind.DirectFile)
            return false;

        if (item.Status is not (DownloadStatus.Failed or DownloadStatus.Cancelled))
            return false;

        var state = TryLoadState(item.Id);
        if (state is null || !File.Exists(state.PartPath))
            return false;

        if (size > 0 && state.TotalBytes > 0 && size != state.TotalBytes)
            return false;

        var wanted = NormalizeName(fileName);
        if (wanted.Length == 0)
            return false;

        foreach (var name in CandidateNames(item, state))
        {
            if (string.Equals(NormalizeName(name), wanted, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Points the item and its saved resume state at <paramref name="newUrl"/> with the new browser
    /// headers. Downloaded bytes and the .part file are untouched; the next run resumes into them.
    /// </summary>
    public static void Apply(DownloadItem item, string newUrl, IReadOnlyDictionary<string, string>? headers)
    {
        DirectDownloadLinkResolver.EnsureSupported(newUrl);

        item.Url = newUrl;
        item.RequestHeaders = headers;
        item.IsLinkExpired = false;
        item.ErrorMessage = string.Empty;

        var state = TryLoadState(item.Id);
        if (state is not null)
        {
            state.Url = newUrl;
            // Empty = resolve again on resume (e.g. a Google Drive page link → its file link).
            state.DownloadUrl = string.Empty;
            FileDownloadStateStore.Save(state);
        }

        item.AppendDiagnostic($"Download address refreshed: {newUrl}");
    }

    private static IEnumerable<string> CandidateNames(DownloadItem item, FileDownloadState state)
    {
        if (!string.IsNullOrWhiteSpace(state.OutputPath))
            yield return Path.GetFileName(state.OutputPath);
        if (!string.IsNullOrWhiteSpace(item.DesiredFileName))
            yield return item.DesiredFileName;
        if (!string.IsNullOrWhiteSpace(item.Title))
            yield return item.Title;
        if (Uri.TryCreate(state.Url, UriKind.Absolute, out var uri))
            yield return Uri.UnescapeDataString(Path.GetFileName(uri.LocalPath));
    }

    /// <summary>File name only, sanitized like saved files, with a " (n)" uniqueness suffix removed.</summary>
    private static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var file = Path.GetFileName(name.Replace('\\', '/').Split('/').Last()).Trim();
        if (file.Length == 0)
            return string.Empty;

        file = FormatHelpers.SanitizeFileName(file);
        var ext = Path.GetExtension(file);
        var stem = Path.GetFileNameWithoutExtension(file);
        var open = stem.LastIndexOf(" (", StringComparison.Ordinal);
        if (open > 0 && stem.EndsWith(')') && int.TryParse(stem[(open + 2)..^1], out _))
            stem = stem[..open];

        return stem + ext;
    }

    private static FileDownloadState? TryLoadState(string itemId)
    {
        try
        {
            return FileDownloadStateStore.Load(itemId);
        }
        catch
        {
            return null;
        }
    }
}
