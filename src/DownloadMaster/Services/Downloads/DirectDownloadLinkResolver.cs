using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed class DirectDownloadLinkResolver
{
    private static readonly HashSet<string> UnsupportedHosts =
    [
        "mega.nz",
        "mega.co.nz",
        "mega.io"
    ];

    public static void EnsureSupported(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Enter a valid http or https download link.");

        var host = uri.Host;
        foreach (var blocked in UnsupportedHosts)
        {
            if (host.Equals(blocked, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException(
                    "MEGA links are not supported. MEGA uses encrypted downloads that require their own client or browser. Use a direct file link instead.");
            }
        }
    }

    public async Task<ResolvedDirectDownload> ResolveAsync(string url, CancellationToken ct)
    {
        EnsureSupported(url);

        if (TryExtractGoogleDriveFileId(url, out var fileId))
            return await ResolveGoogleDriveAsync(url, fileId, ct);

        return new ResolvedDirectDownload
        {
            OriginalUrl = url,
            DownloadUrl = url,
            SupportsRanges = true
        };
    }

    private static async Task<ResolvedDirectDownload> ResolveGoogleDriveAsync(string originalUrl, string fileId, CancellationToken ct)
    {
        using var handler = CreateHandler();
        using var client = CreateClient(handler);

        var downloadUrl = BuildUserContentUrl(fileId, confirm: "t", uuid: null);
        using var first = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        first.EnsureSuccessStatusCode();

        if (!IsHtmlResponse(first))
            return await BuildGoogleDriveResultAsync(originalUrl, downloadUrl, first, fileId, ct);

        var html = await first.Content.ReadAsStringAsync(ct);
        var confirm = ExtractFormValue(html, "confirm") ?? "t";
        var uuid = ExtractFormValue(html, "uuid");
        var fileName = ExtractGoogleDriveFileName(html);

        if (string.IsNullOrWhiteSpace(uuid))
        {
            using var warning = await client.GetAsync(
                $"https://drive.usercontent.google.com/download?id={Uri.EscapeDataString(fileId)}&export=download",
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            if (IsHtmlResponse(warning))
            {
                html = await warning.Content.ReadAsStringAsync(ct);
                confirm = ExtractFormValue(html, "confirm") ?? confirm;
                uuid = ExtractFormValue(html, "uuid");
                fileName ??= ExtractGoogleDriveFileName(html);
            }
        }

        downloadUrl = BuildUserContentUrl(fileId, confirm, uuid);
        using var second = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        second.EnsureSuccessStatusCode();

        if (IsHtmlResponse(second))
        {
            throw new InvalidOperationException(
                "Google Drive did not provide a direct download for that link. Make sure the file is shared as \"Anyone with the link\".");
        }

        return await BuildGoogleDriveResultAsync(originalUrl, downloadUrl, second, fileId, ct, fileName);
    }

    private static async Task<ResolvedDirectDownload> BuildGoogleDriveResultAsync(
        string originalUrl,
        string downloadUrl,
        HttpResponseMessage response,
        string fileId,
        CancellationToken ct,
        string? preferredFileName = null)
    {
        var fileName = preferredFileName;
        if (string.IsNullOrWhiteSpace(fileName) || IsGenericPageName(fileName))
            fileName = ResolveFileName(response, fileId);

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        if (totalBytes <= 0)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            if (stream.CanSeek)
                totalBytes = stream.Length;
        }

        return new ResolvedDirectDownload
        {
            OriginalUrl = originalUrl,
            DownloadUrl = downloadUrl,
            FileName = fileName,
            TotalBytes = totalBytes,
            ContentType = response.Content.Headers.ContentType?.MediaType,
            SupportsRanges = false,
            ForceSingleConnection = true
        };
    }

    private static string BuildUserContentUrl(string fileId, string? confirm, string? uuid)
    {
        var query = new List<string>
        {
            $"id={Uri.EscapeDataString(fileId)}",
            "export=download"
        };

        if (!string.IsNullOrWhiteSpace(confirm))
            query.Add($"confirm={Uri.EscapeDataString(confirm)}");

        if (!string.IsNullOrWhiteSpace(uuid))
            query.Add($"uuid={Uri.EscapeDataString(uuid)}");

        return $"https://drive.usercontent.google.com/download?{string.Join("&", query)}";
    }

    private static string? ExtractFormValue(string html, string name)
    {
        var match = Regex.Match(
            html,
            $@"name=""{Regex.Escape(name)}""\s+value=""([^""]*)""",
            RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractGoogleDriveFileName(string html)
    {
        var match = Regex.Match(
            html,
            @"<span class=""uc-name-size""><a[^>]*>([^<]+)</a>",
            RegexOptions.IgnoreCase);

        if (match.Success)
        {
            var name = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
            if (!string.IsNullOrWhiteSpace(name) && !IsGenericPageName(name))
                return name;
        }

        return null;
    }

    public static bool TryExtractGoogleDriveFileId(string url, out string fileId)
    {
        fileId = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var patterns = new[]
        {
            @"drive\.google\.com/file/d/([a-zA-Z0-9_-]+)",
            @"drive\.google\.com/open\?id=([a-zA-Z0-9_-]+)",
            @"drive\.google\.com/uc(?:\?|&)[^""']*id=([a-zA-Z0-9_-]+)",
            @"drive\.usercontent\.google\.com/download\?(?:[^""']&)?id=([a-zA-Z0-9_-]+)",
            @"docs\.google\.com/[^/]+/d/([a-zA-Z0-9_-]+)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(url, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                fileId = match.Groups[1].Value;
                return true;
            }
        }

        return false;
    }

    private static string ResolveFileName(HttpResponseMessage response, string fileId)
    {
        if (response.Content.Headers.ContentDisposition?.FileName is { } headerName)
        {
            var cleaned = headerName.Trim('"');
            if (!string.IsNullOrWhiteSpace(cleaned) && !IsGenericPageName(cleaned))
                return cleaned;
        }

        if (response.Content.Headers.ContentDisposition?.FileNameStar is { } starName)
        {
            var cleaned = starName.Trim('"');
            if (!string.IsNullOrWhiteSpace(cleaned) && !IsGenericPageName(cleaned))
                return cleaned;
        }

        return $"GoogleDrive_{fileId}.bin";
    }

    private static bool IsGenericPageName(string name) =>
        name.Equals("view", StringComparison.OrdinalIgnoreCase)
        || name.Equals("download", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

    public static bool IsHtmlResponse(HttpResponseMessage response)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        return !string.IsNullOrWhiteSpace(mediaType)
            && mediaType.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task EnsureBinaryResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (IsHtmlResponse(response))
        {
            throw new InvalidOperationException(
                "That link returned a web page instead of a file. Use a direct download URL (ends with the file name), not a sharing page.");
        }

        if (response.Content.Headers.ContentLength is null or 0)
        {
            var peek = new byte[256];
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            if (stream.CanSeek && stream.Length > 0 && stream.Length <= 256)
            {
                var read = await stream.ReadAsync(peek.AsMemory(0, peek.Length), ct);
                if (read > 0 && LooksLikeHtml(peek.AsSpan(0, read)))
                {
                    throw new InvalidOperationException(
                        "That link returned a web page instead of a file. Use a direct download URL, not a sharing page.");
                }
            }
        }
    }

    private static bool LooksLikeHtml(ReadOnlySpan<byte> data)
    {
        var text = System.Text.Encoding.UTF8.GetString(data).TrimStart();
        return text.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("<head", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClientHandler CreateHandler() => new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true,
        UseCookies = true,
        CookieContainer = new CookieContainer()
    };

    private static HttpClient CreateClient(HttpClientHandler handler)
    {
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        return client;
    }
}

public sealed class ResolvedDirectDownload
{
    public string OriginalUrl { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string FileName { get; init; } = "download.bin";
    public long TotalBytes { get; init; }
    public string? ContentType { get; init; }
    public bool SupportsRanges { get; init; } = true;
    public bool ForceSingleConnection { get; init; }
}
