using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DownloadMaster.Services.Bridge;

/// <summary>
/// Tiny HTTP/1.1 server on 127.0.0.1:45780 for the browser extension and for second app instances.
/// Loopback only; browser requests must come from an extension origin, and the Host header must be
/// the loopback address (blocks web pages and DNS-rebinding attacks).
/// </summary>
public sealed class LocalBridgeServer : IDisposable
{
    public const int Port = 45780;
    private const int MaxHeaderBytes = 32 * 1024;
    private const int MaxBodyBytes = 1024 * 1024;

    private readonly Func<BridgeStatus> _status;
    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;

    public LocalBridgeServer(Func<BridgeStatus> status) => _status = status;

    public event Action<BridgeDownloadRequest>? DownloadRequested;
    public event Action<BridgeMediaRequest>? MediaRequested;
    public event Action<BridgeActivateRequest>? ActivateRequested;

    /// <summary>Cookies from the extension; return true when the app was waiting for them.</summary>
    public Func<BridgeCookiesRequest, bool>? CookiesReceived { get; set; }

    /// <summary>Instagram media from the extension; return true when that collection is still wanted.</summary>
    public Func<BridgeInstagramCollectRequest, bool>? InstagramCollectReceived { get; set; }

    public bool IsRunning => _listener is not null;

    /// <summary>Last time the browser extension talked to the app (it checks /status regularly).</summary>
    public DateTime? LastExtensionContactUtc { get; private set; }

    /// <summary>Version the extension reports on /status (?ext=); null for builds older than 2.0.1.</summary>
    public string? ExtensionVersion { get; private set; }

    public bool TryStart(out string? error)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Server.ExclusiveAddressUse = OperatingSystem.IsWindows();
            listener.Start();
            _listener = listener;
            _ = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
            error = null;
            return true;
        }
        catch (SocketException ex)
        {
            error = ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"Port {Port} is already in use; the browser extension cannot connect."
                : ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { /* shutting down */ }
        _listener = null;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client, ct), ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            var stream = client.GetStream();
            var request = await ReadRequestAsync(stream, timeout.Token);
            if (request is null)
                return;

            var response = Route(request);
            await WriteResponseAsync(stream, request, response, timeout.Token);
        }
        catch
        {
            // malformed request or client went away
        }
    }

    private BridgeResponse Route(BridgeRequest request)
    {
        if (!IsLoopbackHost(request.Header("Host")))
            return BridgeResponse.Error(421, "Bad host");

        var origin = request.Header("Origin");
        if (origin is not null && !IsExtensionOrigin(origin))
            return BridgeResponse.Error(403, "Origin not allowed");
        if (origin is not null)
            LastExtensionContactUtc = DateTime.UtcNow;

        if (request.Method == "OPTIONS")
            return new BridgeResponse(204, null);

        var path = request.Path.Split('?')[0].TrimEnd('/');
        switch (request.Method, path)
        {
            case ("GET", "/api/v1/status"):
                if (origin is not null)
                    ExtensionVersion = QueryValue(request.Path, "ext");
                return BridgeResponse.Json(_status(), BridgeJsonContext.Default.BridgeStatus);

            case ("POST", "/api/v1/download"):
            {
                var body = Deserialize(request, BridgeJsonContext.Default.BridgeDownloadRequest);
                if (body is null || !IsHttpUrl(body.Url))
                    return BridgeResponse.Error(400, "A valid http(s) url is required");
                DownloadRequested?.Invoke(body);
                return BridgeResponse.Accepted();
            }

            case ("POST", "/api/v1/media-stream"):
            {
                var body = Deserialize(request, BridgeJsonContext.Default.BridgeMediaRequest);
                if (body is null)
                    return BridgeResponse.Error(400, "Invalid body");
                if (!IsHttpUrl(body.StreamUrl) && IsHttpUrl(body.PageUrl))
                    body.StreamUrl = body.PageUrl!;
                if (!IsHttpUrl(body.StreamUrl))
                    return BridgeResponse.Error(400, "A valid http(s) streamUrl or pageUrl is required");
                MediaRequested?.Invoke(body);
                return BridgeResponse.Accepted();
            }

            case ("POST", "/api/v1/cookies"):
            {
                // Only the extension (never a plain local process or a web page) may hand over cookies,
                // and the app ignores them unless its own "Sync from browser" is waiting.
                if (origin is null)
                    return BridgeResponse.Error(403, "Only the browser extension may send cookies");
                var body = Deserialize(request, BridgeJsonContext.Default.BridgeCookiesRequest);
                if (body is null || string.IsNullOrWhiteSpace(body.Domain))
                    return BridgeResponse.Error(400, "Invalid body");
                return CookiesReceived?.Invoke(body) == true
                    ? BridgeResponse.Accepted()
                    : BridgeResponse.Error(409, "DownloadMaster is not waiting for a login; click Sync from browser in the app");
            }

            case ("POST", "/api/v1/instagram/collect"):
            {
                if (origin is null)
                    return BridgeResponse.Error(403, "Only the browser extension may send Instagram data");
                var body = Deserialize(request, BridgeJsonContext.Default.BridgeInstagramCollectRequest);
                if (body is null || string.IsNullOrWhiteSpace(body.Nonce))
                    return BridgeResponse.Error(400, "Invalid body");
                return InstagramCollectReceived?.Invoke(body) == true
                    ? BridgeResponse.Accepted()
                    : BridgeResponse.Error(409, "DownloadMaster is not reading this Instagram page");
            }

            case ("POST", "/api/v1/activate"):
            {
                // Only local processes (no browser Origin) may wake the app with a command line.
                if (origin is not null)
                    return BridgeResponse.Error(403, "Not allowed from a browser");
                var body = Deserialize(request, BridgeJsonContext.Default.BridgeActivateRequest) ?? new BridgeActivateRequest();
                ActivateRequested?.Invoke(body);
                return BridgeResponse.Accepted();
            }

            default:
                return BridgeResponse.Error(404, "Not found");
        }
    }

    private static T? Deserialize<T>(BridgeRequest request, JsonTypeInfo<T> type) where T : class
    {
        if (request.Body.Length == 0)
            return null;

        try
        {
            return JsonSerializer.Deserialize(request.Body, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? QueryValue(string path, string name)
    {
        var query = path.IndexOf('?') is var q and >= 0 ? path[(q + 1)..] : string.Empty;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0 && pair[..eq] == name)
            {
                var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
                return value.Length is > 0 and <= 32 && value.All(ch => char.IsAsciiDigit(ch) || ch == '.') ? value : null;
            }
        }

        return null;
    }

    private static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static bool IsLoopbackHost(string? host) =>
        host is not null
        && (host.Equals($"127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase)
            || host.Equals($"localhost:{Port}", StringComparison.OrdinalIgnoreCase)
            || host.Equals($"[::1]:{Port}", StringComparison.OrdinalIgnoreCase));

    /// <summary>Chrome, Edge, Brave, Opera use chrome-extension://; Firefox uses moz-extension://.</summary>
    private static bool IsExtensionOrigin(string origin) =>
        origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)
        || origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase)
        || origin.StartsWith("extension://", StringComparison.OrdinalIgnoreCase);

    private static async Task<BridgeRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var head = new MemoryStream();
        int headerEnd;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
                return null;

            head.Write(buffer, 0, read);
            headerEnd = IndexOfHeaderEnd(head.GetBuffer(), (int)head.Length);
            if (headerEnd >= 0)
                break;
            if (head.Length > MaxHeaderBytes)
                return null;
        }

        var all = head.ToArray();
        var headerText = Encoding.ASCII.GetString(all, 0, headerEnd);
        var lines = headerText.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2)
            return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        var contentLength = headers.TryGetValue("Content-Length", out var lengthText)
            && int.TryParse(lengthText, out var length)
            ? length
            : 0;
        if (contentLength is < 0 or > MaxBodyBytes)
            return null;

        var body = new byte[contentLength];
        var already = Math.Min(all.Length - (headerEnd + 4), contentLength);
        Array.Copy(all, headerEnd + 4, body, 0, already);
        var offset = already;
        while (offset < contentLength)
        {
            var read = await stream.ReadAsync(body.AsMemory(offset), ct);
            if (read == 0)
                return null;
            offset += read;
        }

        return new BridgeRequest(requestLine[0].ToUpperInvariant(), requestLine[1], headers, body);
    }

    private static int IndexOfHeaderEnd(byte[] data, int length)
    {
        for (var i = 0; i + 3 < length; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
                return i;
        }

        return -1;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, BridgeRequest request, BridgeResponse response, CancellationToken ct)
    {
        var body = response.Body ?? [];
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(ReasonPhrase(response.StatusCode)).Append("\r\n");
        sb.Append("Content-Type: application/json; charset=utf-8\r\n");
        sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        sb.Append("Cache-Control: no-store\r\n");
        sb.Append("Connection: close\r\n");

        var origin = request.Header("Origin");
        if (origin is not null && IsExtensionOrigin(origin))
        {
            sb.Append("Access-Control-Allow-Origin: ").Append(origin).Append("\r\n");
            sb.Append("Vary: Origin\r\n");
            sb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
            sb.Append("Access-Control-Allow-Headers: Content-Type\r\n");
            sb.Append("Access-Control-Allow-Private-Network: true\r\n");
            sb.Append("Access-Control-Max-Age: 600\r\n");
        }

        sb.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct);
        if (body.Length > 0)
            await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    private static string ReasonPhrase(int code) => code switch
    {
        200 => "OK",
        202 => "Accepted",
        204 => "No Content",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        421 => "Misdirected Request",
        _ => "Error"
    };

    private sealed record BridgeRequest(string Method, string Path, Dictionary<string, string> Headers, byte[] Body)
    {
        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }

    private sealed record BridgeResponse(int StatusCode, byte[]? Body)
    {
        public static BridgeResponse Json<T>(T value, JsonTypeInfo<T> type) =>
            new(200, JsonSerializer.SerializeToUtf8Bytes(value, type));

        public static BridgeResponse Accepted() => new(202, "{\"ok\":true}"u8.ToArray());

        public static BridgeResponse Error(int code, string message) =>
            new(code, Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":" + JsonSerializer.Serialize(message) + "}"));
    }
}
