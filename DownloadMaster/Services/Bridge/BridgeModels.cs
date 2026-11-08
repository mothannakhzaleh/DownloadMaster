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
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class BridgeJsonContext : JsonSerializerContext
{
}
