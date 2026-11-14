using System.Text.Json.Serialization;

namespace DownloadMaster.Services.Bridge;

/// <summary>GET /api/v1/status</summary>
public sealed class BridgeStatus
{
    [JsonPropertyName("running")] public bool Running { get; init; } = true;
    [JsonPropertyName("app")] public string App { get; init; } = "DownloadMaster";
    [JsonPropertyName("version")] public string Version { get; init; } = string.Empty;
    [JsonPropertyName("interceptEnabled")] public bool InterceptEnabled { get; init; }
    [JsonPropertyName("fileTypes")] public IReadOnlyList<string> FileTypes { get; init; } = [];
}

/// <summary>POST /api/v1/download — a browser download handed over by the extension.</summary>
public sealed class BridgeDownloadRequest
{
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("filename")] public string? FileName { get; set; }
    [JsonPropertyName("fileSize")] public long FileSize { get; set; }
    [JsonPropertyName("mimeType")] public string? MimeType { get; set; }
    [JsonPropertyName("referrer")] public string? Referrer { get; set; }
    [JsonPropertyName("userAgent")] public string? UserAgent { get; set; }
    [JsonPropertyName("cookies")] public string? Cookies { get; set; }
    [JsonPropertyName("headers")] public Dictionary<string, string>? Headers { get; set; }
}

/// <summary>POST /api/v1/media-stream — a sniffed HLS/DASH/MP4 stream, or a whole page for yt-dlp.</summary>
public sealed class BridgeMediaRequest
{
    [JsonPropertyName("tabId")] public int TabId { get; set; }
    [JsonPropertyName("pageTitle")] public string? PageTitle { get; set; }
    [JsonPropertyName("pageUrl")] public string? PageUrl { get; set; }
    [JsonPropertyName("streamUrl")] public string StreamUrl { get; set; } = string.Empty;

    /// <summary>hls, dash, video, audio, or page (let yt-dlp read the page).</summary>
    [JsonPropertyName("streamType")] public string StreamType { get; set; } = "page";
    [JsonPropertyName("qualityLabel")] public string? QualityLabel { get; set; }
    [JsonPropertyName("fileSize")] public long FileSize { get; set; }
    [JsonPropertyName("cookies")] public string? Cookies { get; set; }
    [JsonPropertyName("userAgent")] public string? UserAgent { get; set; }
    [JsonPropertyName("referrer")] public string? Referrer { get; set; }
}

/// <summary>One browser cookie as chrome.cookies reports it.</summary>
public sealed class BridgeCookie
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("value")] public string Value { get; set; } = string.Empty;
    [JsonPropertyName("domain")] public string Domain { get; set; } = string.Empty;
    [JsonPropertyName("path")] public string Path { get; set; } = "/";
    [JsonPropertyName("secure")] public bool Secure { get; set; }
    [JsonPropertyName("httpOnly")] public bool HttpOnly { get; set; }
    [JsonPropertyName("hostOnly")] public bool HostOnly { get; set; }
    /// <summary>Unix seconds; 0 = session cookie.</summary>
    [JsonPropertyName("expirationDate")] public long ExpirationDate { get; set; }
}

/// <summary>POST /api/v1/cookies — the extension answering the app's "Sync from browser".</summary>
public sealed class BridgeCookiesRequest
{
    [JsonPropertyName("domain")] public string Domain { get; set; } = string.Empty;
    [JsonPropertyName("cookies")] public List<BridgeCookie> Cookies { get; set; } = [];
}

/// <summary>One file of an Instagram item (a carousel has several).</summary>
public sealed class BridgeInstagramMedia
{
    [JsonPropertyName("type")] public string Type { get; set; } = "image";
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
}

/// <summary>A post, reel, story or highlight story read from Instagram's page by the extension.</summary>
public sealed class BridgeInstagramItem
{
    /// <summary>posts, stories, or highlight:&lt;id&gt;.</summary>
    [JsonPropertyName("section")] public string Section { get; set; } = "posts";
    [JsonPropertyName("sectionTitle")] public string? SectionTitle { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("pk")] public string? Pk { get; set; }
    [JsonPropertyName("takenAt")] public long TakenAt { get; set; }
    [JsonPropertyName("productType")] public string? ProductType { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }
    [JsonPropertyName("media")] public List<BridgeInstagramMedia> Media { get; set; } = [];
}

public sealed class BridgeInstagramHighlight
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string? Title { get; set; }
}

/// <summary>POST /api/v1/instagram/collect — a batch from the extension's Instagram collection.</summary>
public sealed class BridgeInstagramCollectRequest
{
    [JsonPropertyName("nonce")] public string Nonce { get; set; } = string.Empty;
    /// <summary>hello, posts, stories, highlights, single, done.</summary>
    [JsonPropertyName("phase")] public string? Phase { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("done")] public bool Done { get; set; }
    /// <summary>Done, but the posts stopped before the end (a limit, or Instagram stopped loading).</summary>
    [JsonPropertyName("partial")] public bool Partial { get; set; }
    /// <summary>The Instagram tab is hidden or covered, so Instagram is not loading more.</summary>
    [JsonPropertyName("hidden")] public bool Hidden { get; set; }
    [JsonPropertyName("items")] public List<BridgeInstagramItem> Items { get; set; } = [];
    [JsonPropertyName("highlights")] public List<BridgeInstagramHighlight> Highlights { get; set; } = [];
}

/// <summary>POST /api/v1/activate — a second app instance forwarding its command line.</summary>
public sealed class BridgeActivateRequest
{
    [JsonPropertyName("args")] public string[] Args { get; set; } = [];
}

/// <summary>Builds the per-download request headers from what the browser sent.</summary>
public static class BridgeHeaders
{
    public static IReadOnlyDictionary<string, string> Build(
        string? cookies,
        string? referrer,
        string? userAgent,
        IReadOnlyDictionary<string, string>? extra = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (extra is not null)
        {
            foreach (var (name, value) in extra)
            {
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
                    headers[name.Trim()] = value.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(cookies))
            headers["Cookie"] = cookies.Trim();
        if (!string.IsNullOrWhiteSpace(referrer) && Uri.TryCreate(referrer, UriKind.Absolute, out _))
            headers["Referer"] = referrer.Trim();
        if (!string.IsNullOrWhiteSpace(userAgent))
            headers["User-Agent"] = userAgent.Trim();

        return headers;
    }
}

[JsonSerializable(typeof(BridgeStatus))]
[JsonSerializable(typeof(BridgeDownloadRequest))]
[JsonSerializable(typeof(BridgeMediaRequest))]
[JsonSerializable(typeof(BridgeActivateRequest))]
[JsonSerializable(typeof(BridgeCookiesRequest))]
[JsonSerializable(typeof(BridgeInstagramCollectRequest))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class BridgeJsonContext : JsonSerializerContext
{
}
