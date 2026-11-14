using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;

namespace DownloadMaster.Services.Remote;

/// <summary>
/// Remote control for DownloadMaster: a small web page for a phone or another computer plus a JSON
/// API for scripts and the command-line tool. Off by default. Every API call needs the pairing token
/// (Authorization: Bearer &lt;token&gt;); remote clients can only save inside the save folder.
/// <para>
/// Routes: GET / (page) · GET /api/v1/info · GET /api/v1/downloads · POST /api/v1/downloads ·
/// POST /api/v1/downloads/{id}/pause|resume|cancel · DELETE /api/v1/downloads/{id}
/// </para>
/// Plain HTTP: use it on a home network you trust (the token travels unencrypted).
/// </summary>
public sealed class RemoteServer : IDisposable
{
    public const int DefaultPort = 45781;

    // Failed token attempts per client address before it is locked out for a while.
    private const int MaxFailures = 10;
    private static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(2);

    private readonly IRemoteHost _host;
    private readonly string _token;
    private readonly bool _headless;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, (int Count, DateTime Until)> _failures = new();
    private TcpListener? _listener;

    public RemoteServer(IRemoteHost host, string token, bool headless = false)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 16)
            throw new ArgumentException("The pairing token must be at least 16 characters.", nameof(token));
        _host = host;
        _token = token;
        _headless = headless;
    }

    public int Port { get; private set; }
    public bool OnNetwork { get; private set; }
    public bool IsRunning => _listener is not null;

    /// <summary>Starts listening on this computer only, or on every interface when <paramref name="onNetwork"/>.</summary>
    public bool TryStart(int port, bool onNetwork, out string? error)
    {
        try
        {
            var listener = new TcpListener(onNetwork ? IPAddress.Any : IPAddress.Loopback, port);
            listener.Server.ExclusiveAddressUse = OperatingSystem.IsWindows();
            listener.Start();
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            OnNetwork = onNetwork;
            _ = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
            error = null;
            return true;
        }
        catch (SocketException ex)
        {
            error = ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"Port {port} is already in use."
                : ex.Message;
            return false;
        }
    }

    /// <summary>Pairing links to open on the phone (one per network address, or localhost only).</summary>
    public IReadOnlyList<string> PairingLinks()
    {
        var hosts = OnNetwork ? LocalNetworkAddresses() : ["127.0.0.1"];
        return hosts.Select(host => $"http://{host}:{Port}/#token={_token}").ToList();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { /* shutting down */ }
        _listener = null;
    }

    /// <summary>IPv4 addresses of the network adapters that are up (no loopback, no link-local).</summary>
    public static IReadOnlyList<string> LocalNetworkAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(a)
                    && !a.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Select(a => a.ToString())
                .Distinct()
                .ToList();
        }
        catch
        {
            return [];
        }
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
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
            var stream = client.GetStream();
            var request = await MiniHttp.ReadRequestAsync(stream, remote, timeout.Token);
            if (request is null)
                return;

            HttpResponseData response;
            try
            {
                response = await RouteAsync(request);
            }
            catch (Exception ex)
            {
                response = HttpResponseData.Error(500, ex.Message);
            }

            await MiniHttp.WriteResponseAsync(stream, response, timeout.Token);
        }
        catch
        {
            // malformed request or client went away
        }
    }

    /// <summary>Routes one request. Public so the regression tests can drive it without sockets.</summary>
    public async Task<HttpResponseData> RouteAsync(HttpRequestData request)
    {
        var path = request.Path;

        // The page itself holds no data; it asks for the token and then calls the API.
        if (request.Method == "GET" && path is "/" or "/index.html")
            return HttpResponseData.Html(RemotePage.Html);

        if (!path.StartsWith("/api/v1/", StringComparison.Ordinal))
            return HttpResponseData.Error(404, "Not found");

        if (IsLockedOut(request.RemoteAddress))
            return HttpResponseData.Error(429, "Too many wrong tokens; try again in a few minutes");

        if (!RemoteSecurity.TokenMatches(PresentedToken(request), _token))
        {
            RecordFailure(request.RemoteAddress);
            return HttpResponseData.Error(401, "Pairing token missing or wrong");
        }

        _failures.TryRemove(request.RemoteAddress, out _);

        // Browsers send Origin on cross-site requests; only the page served from here may call.
        var origin = request.Header("Origin");
        if (origin is not null && !SameOrigin(origin, request.Header("Host")))
            return HttpResponseData.Error(403, "Origin not allowed");

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries); // api, v1, ...
        switch (request.Method, segments.Length)
        {
            case ("GET", 3) when segments[2] == "info":
                return HttpResponseData.Json(new RemoteInfo
                {
                    Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? string.Empty,
                    Host = Environment.MachineName,
                    Headless = _headless
                }, RemoteJsonContext.Default.RemoteInfo);

            case ("GET", 3) when segments[2] == "downloads":
                return HttpResponseData.Json(await _host.ListAsync(), RemoteJsonContext.Default.IReadOnlyListRemoteDownload);

            case ("POST", 3) when segments[2] == "downloads":
                return await AddAsync(request);

            case ("POST", 5) when segments[2] == "downloads" && segments[4] is "pause" or "resume" or "cancel":
                return await _host.ActAsync(segments[3], segments[4])
                    ? HttpResponseData.Ok()
                    : HttpResponseData.Error(404, "No such download");

            case ("DELETE", 4) when segments[2] == "downloads":
                return await _host.ActAsync(segments[3], "remove")
                    ? HttpResponseData.Ok()
                    : HttpResponseData.Error(404, "No such download");

            default:
                return HttpResponseData.Error(404, "Not found");
        }
    }

    private async Task<HttpResponseData> AddAsync(HttpRequestData request)
    {
        RemoteAddRequest? body = null;
        try
        {
            if (request.Body.Length > 0)
                body = JsonSerializer.Deserialize(request.Body, RemoteJsonContext.Default.RemoteAddRequest);
        }
        catch (JsonException)
        {
            // handled below
        }

        if (body is null || !Uri.TryCreate(body.Url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return HttpResponseData.Error(400, "A valid http(s) url is required");

        var folder = RemoteSecurity.ResolveFolder(_host.SaveFolder, body.Folder);
        if (folder is null)
            return HttpResponseData.Error(400, "The folder must be a sub-folder of the save folder");

        var name = RemoteSecurity.CleanFileName(body.Name);
        var asVideo = body.Video ?? !LinkKind.LooksLikeDirectFile(uri);
        var added = await _host.AddAsync(uri.ToString(), folder, name, asVideo);
        return HttpResponseData.Json(added, RemoteJsonContext.Default.RemoteDownload, 201);
    }

    private static string? PresentedToken(HttpRequestData request)
    {
        var auth = request.Header("Authorization");
        if (auth is not null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth[7..].Trim();
        return request.Header("X-DownloadMaster-Token");
    }

    private static bool SameOrigin(string origin, string? host) =>
        host is not null
        && Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && string.Equals(uri.Authority, host, StringComparison.OrdinalIgnoreCase);

    private bool IsLockedOut(string address) =>
        _failures.TryGetValue(address, out var entry) && entry.Count >= MaxFailures && entry.Until > DateTime.UtcNow;

    private void RecordFailure(string address) =>
        _failures.AddOrUpdate(
            address,
            _ => (1, DateTime.UtcNow + LockoutTime),
            (_, old) => old.Until <= DateTime.UtcNow ? (1, DateTime.UtcNow + LockoutTime) : (old.Count + 1, DateTime.UtcNow + LockoutTime));
}
