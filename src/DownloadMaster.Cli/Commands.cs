using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using DownloadMaster.Models;
using DownloadMaster.Services;
using DownloadMaster.Services.Bridge;
using DownloadMaster.Services.CommandLine;
using DownloadMaster.Services.Remote;

namespace DownloadMaster.Cli;

public static class Commands
{
    // ---- get: download here and now ----------------------------------------------------------

    public static async Task<int> GetAsync(CliArguments a, CancellationToken ct)
    {
        var settings = LoadSettings();
        var current = settings.Current;
        if (a.Connections is { } connections)
            current.FileDownloadConnections = connections;
        if (a.LimitKbps is { } limit)
            current.SpeedLimitKbps = limit;
        if (a.Quality is not null)
            current.DefaultQuality = a.Quality;
        if (a.Format is not null)
            current.PreferredFormat = a.Format;
        // The app's after-download steps (antivirus scan, extraction) run in the background after
        // "Completed"; 'get' exits at that point, so it never starts them.
        current.ScanDownloadsWithAntivirus = false;
        current.ExtractArchivesWhenDone = false;

        var folder = Path.GetFullPath(a.Output ?? Environment.CurrentDirectory);
        var name = RemoteSecurity.CleanFileName(a.Name);
        var url = a.Url!;
        var asVideo = a.Video || !await IsPlainFileAsync(url, a.Headers, ct);
        if (asVideo && !ToolLocator.HasYtDlp)
        {
            Console.Error.WriteLine("This link needs yt-dlp, which was not found (put it in tools/ next to the program, or on PATH).");
            return ExitCodes.Failed;
        }

        // Settings changes above stay in memory: the command line never rewrites the app's settings.
        var manager = new DownloadManager(settings) { ComputeChecksums = false };
        var item = ManagerRemoteHost.CreateItem(url, folder, name, asVideo, current);
        if (a.Headers.Count > 0)
            item.RequestHeaders = a.Headers;

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DownloadItem.Status) && IsFinal(item.Status))
                finished.TrySetResult();
        };

        using var stop = ct.Register(() => manager.Cancel(item));
        if (!a.Quiet)
            Console.Error.WriteLine($"{(asVideo ? "Video" : "File")}: {url}\nSaving to {folder}");

        await manager.EnqueueAsync(item, CancellationToken.None);
        var progress = new ConsoleProgress(a.Quiet);
        while (!finished.Task.IsCompleted)
        {
            progress.Draw(item);
            await Task.WhenAny(finished.Task, Task.Delay(400, CancellationToken.None));
        }

        progress.Done();
        switch (item.Status)
        {
            case DownloadStatus.Completed:
                var path = string.IsNullOrWhiteSpace(item.OutputPath) ? folder : item.OutputPath;
                Console.WriteLine(path);
                if (a.Sha256 && File.Exists(path))
                    Console.WriteLine($"SHA-256 {await FileChecksumService.ComputeSha256Async(path, CancellationToken.None)}");
                return ExitCodes.Ok;
            case DownloadStatus.Cancelled:
                Console.Error.WriteLine("Cancelled.");
                return ExitCodes.Cancelled;
            default:
                Console.Error.WriteLine($"Failed: {(string.IsNullOrWhiteSpace(item.ErrorMessage) ? item.StatusText : item.ErrorMessage)}");
                return ExitCodes.Failed;
        }
    }

    private static bool IsFinal(DownloadStatus status) =>
        status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled;

    /// <summary>
    /// Plain file or web page? Links ending in a file extension are files; anything else is probed
    /// (an HTML answer means a page for yt-dlp).
    /// </summary>
    private static async Task<bool> IsPlainFileAsync(string url, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        if (LinkKind.LooksLikeDirectFile(url))
            return true;
        if (!ToolLocator.HasYtDlp)
            return true; // nothing else could handle it

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, 0);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36");
            foreach (var (key, value) in headers)
                request.Headers.TryAddWithoutValidation(key, value);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var type = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var attachment = response.Content.Headers.ContentDisposition?.DispositionType == "attachment";
            return attachment || (response.IsSuccessStatusCode && !type.Contains("html", StringComparison.OrdinalIgnoreCase));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false; // let yt-dlp try the page
        }
    }

    // ---- add: hand over to the running app or server ----------------------------------------

    public static async Task<int> AddAsync(CliArguments a)
    {
        var folder = a.Output is null ? null : Path.GetFullPath(a.Output);
        var name = RemoteSecurity.CleanFileName(a.Name);

        // 1. The desktop app (or a server on this computer) through the local bridge.
        if (SingleInstance.TryHandOff(LaunchOptions.Build(a.Url!, folder, name, noConfirm: !a.Confirm)))
        {
            Console.WriteLine("Sent to DownloadMaster.");
            return ExitCodes.Ok;
        }

        // 2. A remote-control server on this computer (headless or desktop with remote access on).
        var settings = LoadSettings();
        var token = a.Token ?? settings.Current.RemoteAccessToken;
        var server = a.Server ?? $"127.0.0.1:{a.Port ?? settings.Current.RemoteAccessPort}";
        if (!string.IsNullOrWhiteSpace(token))
        {
            string? relative = null;
            if (a.Output is not null && Path.IsPathRooted(a.Output))
            {
                Console.Error.WriteLine("A server only accepts a sub-folder of its save folder for -o (e.g. -o Linux/ISOs).");
                return ExitCodes.Usage;
            }

            relative = a.Output;
            try
            {
                using var http = RemoteClient(server, token);
                using var response = await http.PostAsJsonAsync("api/v1/downloads",
                    new RemoteAddRequest { Url = a.Url!, Folder = relative, Name = name, Video = a.Video ? true : null },
                    RemoteJsonContext.Default.RemoteAddRequest);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Sent to the DownloadMaster server at {server}.");
                    return ExitCodes.Ok;
                }

                Console.Error.WriteLine($"The server at {server} refused: {await ErrorText(response)}");
                return response.StatusCode == HttpStatusCode.BadRequest ? ExitCodes.Usage : ExitCodes.Unreachable;
            }
            catch (HttpRequestException)
            {
                // nothing listening
            }
        }

        Console.Error.WriteLine("DownloadMaster is not running. Start the app, or use 'get' to download directly.");
        return ExitCodes.Unreachable;
    }

    // ---- status ------------------------------------------------------------------------------

    public static async Task<int> StatusAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var status = await http.GetFromJsonAsync(
                $"http://127.0.0.1:{LocalBridgeServer.Port}/api/v1/status",
                BridgeJsonContext.Default.BridgeStatus);
            if (status?.App == "DownloadMaster")
            {
                Console.WriteLine($"DownloadMaster {status.Version} is running (browser interception {(status.InterceptEnabled ? "on" : "off")}).");
                return ExitCodes.Ok;
            }
        }
        catch
        {
            // not running
        }

        Console.WriteLine("DownloadMaster is not running.");
        return ExitCodes.Unreachable;
    }

    // ---- list --------------------------------------------------------------------------------

    public static async Task<int> ListAsync(CliArguments a)
    {
        var settings = LoadSettings();
        var token = a.Token ?? settings.Current.RemoteAccessToken;
        var server = a.Server ?? $"127.0.0.1:{a.Port ?? settings.Current.RemoteAccessPort}";
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("No pairing token: pass --token, or turn on remote access / run 'serve' on this computer first.");
            return ExitCodes.Unreachable;
        }

        try
        {
            using var http = RemoteClient(server, token);
            using var response = await http.GetAsync("api/v1/downloads");
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"The server at {server} refused: {await ErrorText(response)}");
                return ExitCodes.Unreachable;
            }

            var items = await response.Content.ReadFromJsonAsync(RemoteJsonContext.Default.ListRemoteDownload) ?? [];
            if (items.Count == 0)
            {
                Console.WriteLine("No downloads.");
                return ExitCodes.Ok;
            }

            foreach (var d in items)
            {
                var name = string.IsNullOrWhiteSpace(d.FileName) ? d.Title : d.FileName;
                var details = string.Join("  ", new[] { d.Size, d.Speed, d.Eta }.Where(s => !string.IsNullOrWhiteSpace(s)));
                Console.WriteLine($"{d.Id[..8]}  {d.Status,-11} {d.Progress,5:0.#}%  {name}  {details}".TrimEnd());
            }

            return ExitCodes.Ok;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"No DownloadMaster server at {server} ({ex.Message}).");
            return ExitCodes.Unreachable;
        }
    }

    // ---- serve: headless server ---------------------------------------------------------------

    public static async Task<int> ServeAsync(CliArguments a, string version, CancellationToken ct)
    {
        var settings = LoadSettings();
        var current = settings.Current;
        var token = a.Token;
        if (token is null)
        {
            if (a.NewToken || string.IsNullOrWhiteSpace(current.RemoteAccessToken))
            {
                current.RemoteAccessToken = RemoteSecurity.NewToken();
                settings.Save(current);
            }

            token = current.RemoteAccessToken;
        }
        else if (token.Length < 16)
        {
            Console.Error.WriteLine("--token must be at least 16 characters.");
            return ExitCodes.Usage;
        }

        var saveFolder = Path.GetFullPath(a.Output ?? current.DefaultSavePath);
        Directory.CreateDirectory(saveFolder);
        var manager = new DownloadManager(settings);
        var host = new ManagerRemoteHost(manager, settings, saveFolder);
        host.ItemAdded += item =>
        {
            Log($"Added: {item.Url}");
            item.PropertyChanged += (_, e) => LogStatus(item, e);
        };

        using var server = new RemoteServer(host, token, headless: true);
        var port = a.Port ?? current.RemoteAccessPort;
        if (!server.TryStart(port, a.Network, out var error))
        {
            Console.Error.WriteLine($"Cannot start the server: {error}");
            return ExitCodes.Unreachable;
        }

        // The browser extension and 'add' on this computer talk to the local bridge (if the app isn't using it).
        using var bridge = new LocalBridgeServer(() => new BridgeStatus
        {
            Version = version,
            InterceptEnabled = current.InterceptBrowserDownloads,
            FileTypes = current.InterceptFileTypes
        });
        HookBridge(bridge, host, current, saveFolder);
        var bridgeOn = bridge.TryStart(out var bridgeError);

        Console.WriteLine($"DownloadMaster server {version} — saving to {saveFolder}");
        Console.WriteLine(a.Network
            ? $"Listening on port {server.Port} (home network). Open on your phone:"
            : $"Listening on 127.0.0.1:{server.Port} (this computer only; add --network for your phone). Open:");
        foreach (var link in server.PairingLinks())
            Console.WriteLine($"  {link}");
        Console.WriteLine(bridgeOn
            ? $"Browser extension bridge: 127.0.0.1:{LocalBridgeServer.Port}"
            : $"Browser extension bridge not started: {bridgeError}");
        Console.WriteLine("Keep the pairing link private: it gives full control of this server. Ctrl+C stops.");

        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            // stopping
        }

        foreach (var item in host.Items.Where(i => i.CanCancel))
            manager.Cancel(item); // partial files and resume state stay on disk

        Console.WriteLine("Server stopped.");
        return ExitCodes.Ok;
    }

    private static void HookBridge(LocalBridgeServer bridge, ManagerRemoteHost host, AppSettings settings, string saveFolder)
    {
        bridge.DownloadRequested += request => Queue(() =>
        {
            var name = RemoteSecurity.CleanFileName(request.FileName);
            var item = ManagerRemoteHost.CreateItem(request.Url, saveFolder, name, asVideo: false, settings);
            item.RequestHeaders = BridgeHeaders.Build(request.Cookies, request.Referrer, request.UserAgent, request.Headers);
            return host.AddItemAsync(item);
        });
        bridge.MediaRequested += request => Queue(() =>
        {
            var item = ManagerRemoteHost.CreateItem(request.StreamUrl, saveFolder, null, asVideo: true, settings);
            item.RequestHeaders = BridgeHeaders.Build(request.Cookies, request.Referrer, request.UserAgent);
            return host.AddItemAsync(item);
        });
        bridge.ActivateRequested += request => Queue(async () =>
        {
            var launch = LaunchOptions.Parse(request.Args);
            if (launch.Url is null)
                return;
            // Command-line folders must still stay inside the save folder.
            var folder = launch.Folder is null ? saveFolder : RemoteSecurity.ResolveFolder(saveFolder, Path.GetRelativePath(saveFolder, launch.Folder));
            if (folder is null)
            {
                Log($"Refused {launch.Url}: folder {launch.Folder} is outside {saveFolder}");
                return;
            }

            await host.AddAsync(launch.Url, folder, RemoteSecurity.CleanFileName(launch.FileName), !LinkKind.LooksLikeDirectFile(launch.Url) && launch.FileName is null);
        });
    }

    private static void Queue(Func<Task> work) => _ = Task.Run(async () =>
    {
        try { await work(); }
        catch (Exception ex) { Log($"Could not add: {ex.Message}"); }
    });

    private static void LogStatus(DownloadItem item, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DownloadItem.Status))
            return;
        var title = string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title;
        switch (item.Status)
        {
            case DownloadStatus.Completed:
                Log($"Done: {title} → {item.OutputPath}");
                break;
            case DownloadStatus.Failed:
                Log($"Failed: {title} — {item.ErrorMessage}");
                break;
            case DownloadStatus.Cancelled:
                Log($"Cancelled: {title}");
                break;
        }
    }

    private static void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

    // ---- helpers -----------------------------------------------------------------------------

    private static SettingsService LoadSettings()
    {
        var settings = new SettingsService();
        settings.Load();
        return settings;
    }

    private static HttpClient RemoteClient(string server, string token)
    {
        var baseUri = server.Contains("://", StringComparison.Ordinal) ? server : $"http://{server}";
        var http = new HttpClient { BaseAddress = new Uri(baseUri.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<string> ErrorText(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return $"{(int)response.StatusCode} {body}".Trim();
    }
}

/// <summary>One-line progress on stderr (plain lines when stderr is redirected).</summary>
internal sealed class ConsoleProgress(bool quiet)
{
    private readonly bool _interactive = !Console.IsErrorRedirected;
    private DateTime _lastPlain = DateTime.MinValue;
    private int _lastLength;

    public void Draw(DownloadItem item)
    {
        if (quiet)
            return;

        var percent = Math.Clamp(item.Progress, 0, 100);
        const int width = 24;
        var filled = (int)Math.Round(percent / 100 * width);
        var text = new StringBuilder()
            .Append('[').Append('#', filled).Append('.', width - filled).Append("] ")
            .Append($"{percent,5:0.0}%");
        foreach (var part in new[] { item.SizeText, item.SpeedText, string.IsNullOrWhiteSpace(item.EtaText) ? null : "ETA " + item.EtaText, item.StatusText })
        {
            if (!string.IsNullOrWhiteSpace(part))
                text.Append("  ").Append(part);
        }

        var line = text.ToString();
        if (_interactive)
        {
            var max = Math.Max(20, SafeWidth() - 1);
            if (line.Length > max)
                line = line[..max];
            Console.Error.Write("\r" + line.PadRight(_lastLength));
            _lastLength = line.Length;
        }
        else if (DateTime.UtcNow - _lastPlain > TimeSpan.FromSeconds(5))
        {
            Console.Error.WriteLine(line);
            _lastPlain = DateTime.UtcNow;
        }
    }

    public void Done()
    {
        if (!quiet && _interactive && _lastLength > 0)
            Console.Error.WriteLine();
    }

    private static int SafeWidth()
    {
        try { return Console.WindowWidth; } catch { return 100; }
    }
}
