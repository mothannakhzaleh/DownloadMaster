using System.Text;

namespace DownloadMaster.Services.Bridge;

/// <summary>
/// Hands Instagram cookies from the browser extension to "Sync from browser".
/// Chrome/Edge 127+ encrypt their cookie store so that only the browser itself can read it; the
/// extension can, so the app asks for them instead of reading the database. Cookies are accepted only
/// while a sync is waiting, and only for instagram.com.
/// </summary>
public static class ExtensionCookieRelay
{
    public const string Domain = "instagram.com";
    public const string SyncUrl = "https://www.instagram.com/#downloadmaster-sync";

    private static readonly object Gate = new();
    private static TaskCompletionSource<BridgeCookiesRequest>? _pending;

    /// <summary>True when the local bridge is listening, so the extension can reach the app.</summary>
    public static Func<bool> BridgeAvailable { get; set; } = static () => false;

    /// <summary>Called by the bridge; true when a sync was waiting and took the cookies.</summary>
    public static bool Deliver(BridgeCookiesRequest request)
    {
        if (!string.Equals(request.Domain, Domain, StringComparison.OrdinalIgnoreCase))
            return false;

        TaskCompletionSource<BridgeCookiesRequest>? pending;
        lock (Gate)
        {
            pending = _pending;
            _pending = null;
        }

        return pending?.TrySetResult(request) == true;
    }

    /// <summary>Waits for the extension; null on timeout or cancel.</summary>
    public static async Task<BridgeCookiesRequest?> WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<BridgeCookiesRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Gate)
        {
            _pending?.TrySetCanceled();
            _pending = tcs;
        }

        try
        {
            return await tcs.Task.WaitAsync(timeout, ct);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            return null;
        }
        finally
        {
            lock (Gate)
            {
                if (ReferenceEquals(_pending, tcs))
                    _pending = null;
            }
        }
    }

    /// <summary>Netscape cookies.txt, the format yt-dlp and the Instagram downloader read.</summary>
    public static string ToNetscape(IEnumerable<BridgeCookie> cookies)
    {
        var sb = new StringBuilder("# Netscape HTTP Cookie File\n# Exported by DownloadMaster from the browser extension\n\n");
        foreach (var c in cookies)
        {
            if (string.IsNullOrEmpty(c.Name) || string.IsNullOrEmpty(c.Domain)
                || HasControlChars(c.Name) || HasControlChars(c.Value) || HasControlChars(c.Domain) || HasControlChars(c.Path))
                continue;

            var domain = c.HostOnly ? c.Domain.TrimStart('.') : (c.Domain.StartsWith('.') ? c.Domain : "." + c.Domain);
            // No "#HttpOnly_" prefix: the app's own cookie readers skip '#' lines, and yt-dlp doesn't need it.
            sb.Append(domain).Append('\t')
              .Append(c.HostOnly ? "FALSE" : "TRUE").Append('\t')
              .Append(string.IsNullOrEmpty(c.Path) ? "/" : c.Path).Append('\t')
              .Append(c.Secure ? "TRUE" : "FALSE").Append('\t')
              .Append(Math.Max(0, c.ExpirationDate)).Append('\t')
              .Append(c.Name).Append('\t')
              .Append(c.Value).Append('\n');
        }

        return sb.ToString();
    }

    private static bool HasControlChars(string? text) =>
        text is not null && text.Any(ch => ch is '\t' or '\r' or '\n');
}
