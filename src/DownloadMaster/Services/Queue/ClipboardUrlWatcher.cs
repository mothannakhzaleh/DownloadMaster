using System.IO;

namespace DownloadMaster.Services.Queue;

/// <summary>
/// Decides whether polled clipboard text is a NEW downloadable link. The first observation is the
/// baseline (whatever was on the clipboard at startup is ignored); repeats of the last text are ignored.
/// </summary>
public sealed class ClipboardUrlWatcher
{
    private bool _hasBaseline;
    private string? _last;

    /// <summary>
    /// Feeds the current clipboard text. Returns the URL when it changed to an http(s) link whose
    /// path ends in one of <paramref name="fileTypes"/> (extensions without the dot); otherwise null.
    /// </summary>
    public string? Observe(string? text, IEnumerable<string> fileTypes)
    {
        var current = text?.Trim();
        if (!_hasBaseline)
        {
            _hasBaseline = true;
            _last = current;
            return null;
        }

        if (string.Equals(current, _last, StringComparison.Ordinal))
            return null;

        _last = current;
        return IsDownloadLink(current, fileTypes) ? current : null;
    }

    /// <summary>Marks text as already seen (e.g. a link the app itself put on the clipboard).</summary>
    public void Suppress(string? text)
    {
        _hasBaseline = true;
        _last = text?.Trim();
    }

    public static bool IsDownloadLink(string? text, IEnumerable<string> fileTypes)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096 || text.Contains('\n') || text.Contains(' '))
            return false;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return false;

        string ext;
        try
        {
            ext = Path.GetExtension(Uri.UnescapeDataString(uri.AbsolutePath)).TrimStart('.');
        }
        catch
        {
            return false;
        }

        if (ext.Length == 0)
            return false;

        return fileTypes.Any(t => string.Equals(t?.Trim().TrimStart('.'), ext, StringComparison.OrdinalIgnoreCase));
    }

    public static string FileNameFromUrl(string url)
    {
        try
        {
            var name = Uri.UnescapeDataString(Path.GetFileName(new Uri(url).AbsolutePath));
            return string.IsNullOrWhiteSpace(name) ? "download" : name;
        }
        catch
        {
            return "download";
        }
    }
}
