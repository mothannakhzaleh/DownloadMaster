// Regressions for downloadmaster-cli, the /d /p /f /n launch switches and the remote-control server.
// Exit code 0 = all checks passed, 1 = a check failed (CI friendly).
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using DownloadEngine.Regression;
using DownloadMaster.Cli;
using DownloadMaster.Models;
using DownloadMaster.Services;
using DownloadMaster.Services.CommandLine;
using DownloadMaster.Services.Remote;

var repoRoot = FindRepoRoot();
var runDir = Path.Combine(repoRoot, "test-output", "cli-regression", $"run-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}");
var dataDir = Path.Combine(runDir, "data");
var saveDir = Path.Combine(runDir, "downloads");
Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(saveDir);
// Before AppPaths is touched: settings and resume state stay inside test-output.
Environment.SetEnvironmentVariable(AppPaths.DataDirVariable, dataDir);
Console.WriteLine($"CLI regression output: {runDir}");

var failures = new List<string>();
var passed = 0;
await using var files = new RangeServer();

// ---- launch switches ---------------------------------------------------------------------

await Run("classic /d /p /f /n switches", () =>
{
    var o = LaunchOptions.Parse(["/d", "https://example.com/a.zip", "/p", @"C:\Down loads", "/f", "b.zip", "/n"]);
    Check(o.Url == "https://example.com/a.zip", $"url {o.Url}");
    Check(o.Folder == @"C:\Down loads", $"folder {o.Folder}");
    Check(o.FileName == "b.zip", $"name {o.FileName}");
    Check(o.NoConfirm && !o.ResumeAll && o.HasWork, "flags");
    return Task.CompletedTask;
});

await Run("long switches, bare URL, /s and unknown switches", () =>
{
    var o = LaunchOptions.Parse(["--url", "http://x.test/v", "--no-confirm", "--minimized"]);
    Check(o.Url == "http://x.test/v" && o.NoConfirm && o.StartMinimized, "long form");
    var bare = LaunchOptions.Parse(["https://example.com/page"]);
    Check(bare.Url == "https://example.com/page" && !bare.NoConfirm, "bare url");
    var s = LaunchOptions.Parse(["/s"]);
    Check(s.ResumeAll && s.Url is null && s.HasWork, "/s");
    var odd = LaunchOptions.Parse(["/home/me", "/d", "ftp://x"]);
    Check(odd.Url is null && odd.Ignored.Count == 2, $"a path is not a switch, ftp refused (ignored {odd.Ignored.Count})");
    var round = LaunchOptions.Parse(LaunchOptions.Build("https://e.test/f.iso", "/tmp/x", "f.iso", noConfirm: true));
    Check(round.Url == "https://e.test/f.iso" && round.Folder == "/tmp/x" && round.FileName == "f.iso" && round.NoConfirm, "Build round trip");
    return Task.CompletedTask;
});

// ---- CLI argument parsing -----------------------------------------------------------------

await Run("CLI arguments", () =>
{
    var a = CliArguments.Parse(["get", "https://e.test/f.zip", "-o", "out", "-c", "4", "--limit", "500", "-H", "Cookie: a=b", "--sha256"]);
    Check(a.Command == "get" && a.Url == "https://e.test/f.zip" && a.Output == "out", "basic");
    Check(a.Connections == 4 && a.LimitKbps == 500 && a.Sha256 && a.Headers["Cookie"] == "a=b", "options");
    var s = CliArguments.Parse(["serve", "--network", "-p", "5000"]);
    Check(s.Network && s.Port == 5000 && s.Url is null, "serve");
    ExpectUsage(["get"], "get without a link");
    ExpectUsage(["get", "ftp://x/y"], "non-http link");
    ExpectUsage(["get", "https://e.test/a", "--bogus"], "unknown option");
    ExpectUsage(["get", "https://e.test/a", "-c", "99"], "connections out of range");
    ExpectUsage(["serve", "https://e.test/a"], "serve with a link");
    ExpectUsage(["fly"], "unknown command");
    return Task.CompletedTask;
});

// ---- remote security rules ----------------------------------------------------------------

await Run("save-folder confinement and file names", () =>
{
    var root = Path.Combine(runDir, "root");
    Check(RemoteSecurity.ResolveFolder(root, null) == Path.GetFullPath(root), "empty = root");
    Check(RemoteSecurity.ResolveFolder(root, "ISOs/Linux") == Path.GetFullPath(Path.Combine(root, "ISOs", "Linux")), "sub-folder");
    foreach (var bad in new[] { "..", "../x", "a/../../x", "a/./b", "/etc", "\\\\server\\share", "C:\\Windows", "C:x" })
        Check(RemoteSecurity.ResolveFolder(root, bad) is null, $"'{bad}' must be refused");
    Check(RemoteSecurity.CleanFileName("../../evil.exe") == "evil.exe", "path parts stripped");
    Check(RemoteSecurity.CleanFileName(@"..\..\evil.exe") == "evil.exe", "windows path parts stripped");
    Check(RemoteSecurity.CleanFileName("  ..  ") is null, "dots only");
    Check(RemoteSecurity.NewToken().Length == 32 && RemoteSecurity.NewToken() != RemoteSecurity.NewToken(), "token");
    Check(RemoteSecurity.TokenMatches("abc", "abc") && !RemoteSecurity.TokenMatches("abd", "abc") && !RemoteSecurity.TokenMatches(null, "abc"), "token compare");
    return Task.CompletedTask;
});

// ---- remote server over real sockets (loopback) -------------------------------------------

var token = RemoteSecurity.NewToken();
var settings = new SettingsService();
settings.Load();
settings.Current.FileDownloadConnections = 4;
var manager = new DownloadManager(settings) { ComputeChecksums = false };
var host = new ManagerRemoteHost(manager, settings, saveDir);
using var remote = new RemoteServer(host, token, headless: true);
Check(remote.TryStart(0, onNetwork: false, out var startError), $"server start: {startError}");
using var anon = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{remote.Port}/") };
using var paired = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{remote.Port}/") };
paired.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

await Run("page is public, API needs the token", async () =>
{
    using var page = await anon.GetAsync("/");
    Check(page.StatusCode == HttpStatusCode.OK && (await page.Content.ReadAsStringAsync()).Contains("DownloadMaster"), "page");
    Check(page.Headers.TryGetValues("Content-Security-Policy", out _) || page.Content.Headers.Contains("Content-Security-Policy")
          || (await RawHeaders(remote.Port, "/")).Contains("Content-Security-Policy"), "CSP header");
    using var none = await anon.GetAsync("api/v1/downloads");
    Check(none.StatusCode == HttpStatusCode.Unauthorized, $"no token → {(int)none.StatusCode}");
    using var wrong = new HttpRequestMessage(HttpMethod.Get, "api/v1/downloads");
    wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token[..^1] + "x");
    using var wrongResponse = await anon.SendAsync(wrong);
    Check(wrongResponse.StatusCode == HttpStatusCode.Unauthorized, $"wrong token → {(int)wrongResponse.StatusCode}");
    using var ok = await paired.GetAsync("api/v1/downloads");
    Check(ok.StatusCode == HttpStatusCode.OK, $"paired → {(int)ok.StatusCode}");
    using var info = await paired.GetAsync("api/v1/info");
    Check((await info.Content.ReadAsStringAsync()).Contains("\"headless\":true"), "info");
});

await Run("cross-site origin refused, bad bodies refused", async () =>
{
    using var cross = new HttpRequestMessage(HttpMethod.Get, "api/v1/downloads");
    cross.Headers.Add("Origin", "http://evil.test");
    using var crossResponse = await paired.SendAsync(cross);
    Check(crossResponse.StatusCode == HttpStatusCode.Forbidden, $"cross origin → {(int)crossResponse.StatusCode}");
    using var same = new HttpRequestMessage(HttpMethod.Get, "api/v1/downloads");
    same.Headers.Add("Origin", $"http://127.0.0.1:{remote.Port}");
    using var sameResponse = await paired.SendAsync(same);
    Check(sameResponse.StatusCode == HttpStatusCode.OK, $"same origin → {(int)sameResponse.StatusCode}");

    using var escape = await paired.PostAsJsonAsync("api/v1/downloads", new RemoteAddRequest { Url = files.Url("/x.bin"), Folder = "../outside" }, RemoteJsonContext.Default.RemoteAddRequest);
    Check(escape.StatusCode == HttpStatusCode.BadRequest, $"folder escape → {(int)escape.StatusCode}");
    using var notHttp = await paired.PostAsJsonAsync("api/v1/downloads", new RemoteAddRequest { Url = "file:///etc/passwd" }, RemoteJsonContext.Default.RemoteAddRequest);
    Check(notHttp.StatusCode == HttpStatusCode.BadRequest, $"file:// → {(int)notHttp.StatusCode}");
    using var garbage = await paired.PostAsync("api/v1/downloads", new StringContent("{not json", Encoding.UTF8, "application/json"));
    Check(garbage.StatusCode == HttpStatusCode.BadRequest, $"bad json → {(int)garbage.StatusCode}");
    using var unknown = await paired.PostAsync("api/v1/downloads/nope/pause", null);
    Check(unknown.StatusCode == HttpStatusCode.NotFound, $"unknown id → {(int)unknown.StatusCode}");
});

await Run("headless queue downloads into a confined sub-folder", async () =>
{
    var data = MakeData(3 * 1024 * 1024 + 77, seed: 7);
    files.Files["/remote.bin"] = data;
    using var added = await paired.PostAsJsonAsync("api/v1/downloads",
        new RemoteAddRequest { Url = files.Url("/remote.bin"), Folder = "Remote/Sub", Name = "../renamed.bin" },
        RemoteJsonContext.Default.RemoteAddRequest);
    Check(added.StatusCode == HttpStatusCode.Created, $"add → {(int)added.StatusCode} {await added.Content.ReadAsStringAsync()}");
    var row = await added.Content.ReadFromJsonAsync(RemoteJsonContext.Default.RemoteDownload);
    Check(row is { Kind: "file" }, "guessed as a plain file");

    RemoteDownload? done = null;
    for (var i = 0; i < 150 && done is null; i++)
    {
        await Task.Delay(100);
        var list = await paired.GetFromJsonAsync("api/v1/downloads", RemoteJsonContext.Default.ListRemoteDownload) ?? [];
        done = list.FirstOrDefault(d => d.Id == row!.Id && d.Status is "Completed" or "Failed");
    }

    Check(done?.Status == "Completed", $"status {done?.Status ?? "timeout"} {done?.StatusText}");
    var expected = Path.Combine(saveDir, "Remote", "Sub", "renamed.bin");
    Check(File.Exists(expected), $"file at {expected}");
    Check(File.Exists(expected) && Sha(File.ReadAllBytes(expected)) == Sha(data), "content matches");
    Check(done?.FileName == "renamed.bin", $"API shows the file name only ({done?.FileName})");

    using var removed = await paired.DeleteAsync($"api/v1/downloads/{row!.Id}");
    Check(removed.StatusCode == HttpStatusCode.OK, "remove");
    var after = await paired.GetFromJsonAsync("api/v1/downloads", RemoteJsonContext.Default.ListRemoteDownload) ?? [];
    Check(after.All(d => d.Id != row.Id), "removed from the list");
});

await Run("wrong tokens lock the client out", async () =>
{
    // A separate server so the lockout doesn't affect the checks above.
    using var locked = new RemoteServer(host, RemoteSecurity.NewToken());
    Check(locked.TryStart(0, false, out _), "start");
    using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{locked.Port}/") };
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "0123456789abcdef0123456789abcdef");
    var codes = new List<int>();
    for (var i = 0; i < 12; i++)
    {
        using var r = await client.GetAsync("api/v1/downloads");
        codes.Add((int)r.StatusCode);
    }

    Check(codes.Take(10).All(c => c == 401), $"first ten 401 ({string.Join(",", codes)})");
    Check(codes.Skip(10).All(c => c == 429), "then 429");
});

// ---- the real CLI process ------------------------------------------------------------------

await Run("downloadmaster-cli get downloads and prints the path", async () =>
{
    var data = MakeData(5 * 1024 * 1024 + 3, seed: 11);
    files.Files["/cli/file.bin"] = data;
    var outDir = Path.Combine(runDir, "cli-get");
    var (code, stdout, stderr) = await RunCli(["get", files.Url("/cli/file.bin"), "-o", outDir, "-c", "4", "--sha256", "-q"]);
    Check(code == 0, $"exit {code}: {stderr}");
    var path = Path.Combine(outDir, "file.bin");
    Check(stdout.Contains(path), $"stdout has the path: {stdout}");
    Check(stdout.Contains("SHA-256 " + Sha(data)), "stdout has the SHA-256");
    Check(File.Exists(path) && Sha(File.ReadAllBytes(path)) == Sha(data), "content matches");
});

await Run("downloadmaster-cli exit codes", async () =>
{
    var (usage, _, _) = await RunCli(["get"]);
    Check(usage == 2, $"usage error → {usage}");
    files.Forbidden["/cli/forbidden.bin"] = true;
    files.Files["/cli/forbidden.bin"] = MakeData(1000, 3);
    var (failed, _, err) = await RunCli(["get", files.Url("/cli/forbidden.bin"), "-o", Path.Combine(runDir, "cli-fail"), "-q"]);
    Check(failed == 1, $"403 → exit {failed} ({err.Trim()})");
    var (help, helpOut, _) = await RunCli(["--help"]);
    Check(help == 0 && helpOut.Contains("serve"), "help");
});

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failures.Count} failed");
foreach (var failure in failures)
    Console.WriteLine($"  FAIL {failure}");
return failures.Count == 0 ? 0 : 1;

// ---- helpers -------------------------------------------------------------------------------

async Task Run(string name, Func<Task> body)
{
    try
    {
        await body();
        passed++;
        Console.WriteLine($"  ok   {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"  FAIL {name}: {ex.Message}");
    }
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

static void ExpectUsage(string[] args, string what)
{
    try
    {
        CliArguments.Parse(args);
    }
    catch (CliUsageException)
    {
        return;
    }

    throw new Exception($"{what} should be a usage error");
}

async Task<(int Code, string Out, string Err)> RunCli(string[] cliArgs)
{
    var dll = Path.Combine(AppContext.BaseDirectory, "downloadmaster-cli.dll");
    var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    info.ArgumentList.Add(dll);
    foreach (var arg in cliArgs)
        info.ArgumentList.Add(arg);
    info.Environment[AppPaths.DataDirVariable] = dataDir;
    using var process = Process.Start(info)!;
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    await process.WaitForExitAsync(timeout.Token);
    return (process.ExitCode, await stdout, await stderr);
}

static async Task<string> RawHeaders(int port, string path)
{
    using var tcp = new System.Net.Sockets.TcpClient();
    await tcp.ConnectAsync(IPAddress.Loopback, port);
    var stream = tcp.GetStream();
    await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\n\r\n"));
    using var reader = new StreamReader(stream);
    var text = await reader.ReadToEndAsync();
    return text.Split("\r\n\r\n")[0];
}

static byte[] MakeData(int length, int seed)
{
    var data = new byte[length];
    new Random(seed).NextBytes(data);
    return data;
}

static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "DownloadMaster.slnx")) || File.Exists(Path.Combine(dir.FullName, "DownloadMaster.sln")))
            return dir.FullName;
        dir = dir.Parent;
    }

    return Directory.GetCurrentDirectory();
}
