using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using DownloadMaster.Models;

namespace DownloadMaster.Services.Remote;

/// <summary>
/// What the remote server needs from whoever owns the download list: the desktop window or the
/// headless server. Calls may arrive on any thread; implementations marshal as they need.
/// </summary>
public interface IRemoteHost
{
    Task<IReadOnlyList<RemoteDownload>> ListAsync();

    /// <summary>Queue a link. <paramref name="folder"/> is already confined to the save folder.</summary>
    Task<RemoteDownload> AddAsync(string url, string folder, string? fileName, bool asVideo);

    /// <summary>pause, resume, cancel or remove. False when the id is unknown.</summary>
    Task<bool> ActAsync(string id, string action);

    string SaveFolder { get; }
}

/// <summary>One row of GET /api/v1/downloads.</summary>
public sealed class RemoteDownload
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
    [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("statusText")] public string StatusText { get; init; } = string.Empty;
    [JsonPropertyName("progress")] public double Progress { get; init; }
    [JsonPropertyName("speed")] public string Speed { get; init; } = string.Empty;
    [JsonPropertyName("eta")] public string Eta { get; init; } = string.Empty;
    [JsonPropertyName("size")] public string Size { get; init; } = string.Empty;
    /// <summary>File name only — local paths are never sent to remote clients.</summary>
    [JsonPropertyName("fileName")] public string FileName { get; init; } = string.Empty;
    [JsonPropertyName("canPause")] public bool CanPause { get; init; }
    [JsonPropertyName("canResume")] public bool CanResume { get; init; }
    [JsonPropertyName("canCancel")] public bool CanCancel { get; init; }

    public static RemoteDownload From(DownloadItem item) => new()
    {
        Id = item.Id,
        Title = string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title,
        Url = item.Url,
        Kind = item.Kind == DownloadKind.DirectFile ? "file" : "video",
        Status = item.Status.ToString(),
        StatusText = item.StatusText,
        Progress = Math.Round(item.Progress, 1),
        Speed = item.SpeedText,
        Eta = item.EtaText,
        Size = item.SizeText,
        FileName = string.IsNullOrWhiteSpace(item.OutputPath) ? string.Empty : Path.GetFileName(item.OutputPath),
        CanPause = item.CanPause,
        CanResume = item.CanResume || item.CanRetry,
        CanCancel = item.CanCancel
    };
}

/// <summary>POST /api/v1/downloads</summary>
public sealed class RemoteAddRequest
{
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    /// <summary>Optional sub-folder of the save folder (never an absolute path).</summary>
    [JsonPropertyName("folder")] public string? Folder { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    /// <summary>Force the video/page path (yt-dlp) instead of guessing from the link.</summary>
    [JsonPropertyName("video")] public bool? Video { get; set; }
}

/// <summary>GET /api/v1/info</summary>
public sealed class RemoteInfo
{
    [JsonPropertyName("app")] public string App { get; init; } = "DownloadMaster";
    [JsonPropertyName("version")] public string Version { get; init; } = string.Empty;
    [JsonPropertyName("host")] public string Host { get; init; } = string.Empty;
    [JsonPropertyName("headless")] public bool Headless { get; init; }
}

[JsonSerializable(typeof(RemoteDownload))]
[JsonSerializable(typeof(List<RemoteDownload>))]
[JsonSerializable(typeof(IReadOnlyList<RemoteDownload>))]
[JsonSerializable(typeof(RemoteAddRequest))]
[JsonSerializable(typeof(RemoteInfo))]
public sealed partial class RemoteJsonContext : JsonSerializerContext
{
}

/// <summary>Pairing tokens and remote save-folder rules.</summary>
public static class RemoteSecurity
{
    /// <summary>A new random pairing token (32 hex characters, 128 bits).</summary>
    public static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    /// <summary>Constant-time comparison so the token can't be guessed byte by byte from timings.</summary>
    public static bool TokenMatches(string? presented, string expected)
    {
        if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(expected))
            return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>
    /// The folder a remote client may save into: the save folder itself, or a sub-folder of it.
    /// Returns null for absolute paths, drive letters, "..", or anything that resolves outside.
    /// </summary>
    public static string? ResolveFolder(string saveFolder, string? requested)
    {
        var root = Path.GetFullPath(saveFolder);
        if (string.IsNullOrWhiteSpace(requested))
            return root;

        var relative = requested.Trim().Replace('\\', '/');
        if (relative.StartsWith('/') || relative.Contains(':') || Path.IsPathRooted(relative))
            return null;
        if (relative.Split('/').Any(part => part is ".." or "."))
            return null;
        if (relative.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return null;

        var full = Path.GetFullPath(Path.Combine(root, relative));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.StartsWith(rootWithSeparator, comparison) ? full : null;
    }

    /// <summary>A safe file name (no folders), or null when nothing usable remains.</summary>
    public static string? CleanFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var leaf = Path.GetFileName(name.Replace('\\', '/').Split('/').Last().Trim());
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(leaf.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
        return cleaned.Length == 0 ? null : cleaned;
    }
}
