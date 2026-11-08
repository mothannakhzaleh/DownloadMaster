// Headless regressions for DownloadMaster's direct-file engine (HttpFileDownloadService):
// integrity, multi-connection + work stealing, crash-safe resume (real process kill), 403 →
// "link expired" → refresh address, the global speed limiter and 206 probe sizing.
// Exit code 0 = all checks passed, 1 = a check failed (CI friendly).
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using DownloadEngine.Regression;
using DownloadMaster.Models;
using DownloadMaster.Services;

const int MB = 1024 * 1024;

var repoRoot = FindRepoRoot();
if (args.Length > 0 && args[0] == "--child")
    return await RunChildAsync(args);

var runDir = Path.Combine(repoRoot, "test-output", "engine-regression", $"run-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}");
var dataDir = Path.Combine(runDir, "data");
var saveDir = Path.Combine(runDir, "downloads");
Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(saveDir);
// Must be set before AppPaths is first touched: resume state goes inside test-output, never the real profile.
Environment.SetEnvironmentVariable(AppPaths.DataDirVariable, dataDir);
Console.WriteLine($"Engine regression output: {runDir}");

var failures = new List<string>();
var passed = 0;

await using var server = new RangeServer();
var service = new HttpFileDownloadService();

await Run("206 probe reports the full size", async () =>
{
    var data = MakeData(10 * MB + 123, seed: 1);
    server.Files["/probe.bin"] = data;
    var probe = await service.ProbeAsync(server.Url("/probe.bin"), CancellationToken.None);
    Check(probe.TotalBytes == data.Length, $"probe size {probe.TotalBytes} != {data.Length} (206 Content-Length is 1)");
    Check(probe.SupportsRanges, "probe should detect range support");
    Check(probe.FileName == "probe.bin", $"probe file name '{probe.FileName}'");
    Check(server.RequestsFor("/probe.bin").Any(r => r is { Status: 206, RangeStart: 0, RangeEnd: 0 }), "probe should ask for bytes=0-0");
});

await Run("8-connection download is byte-exact (SHA-256)", async () =>
{
    var data = MakeData(16 * MB + 777, seed: 2);
    server.Files["/full.bin"] = data;
    var item = NewItem(server.Url("/full.bin"), connections: 8);
    await service.DownloadAsync(item, 8, null, null, CancellationToken.None);

    Check(File.Exists(item.OutputPath), "output file missing");
    Check(!File.Exists(item.OutputPath + ".part"), ".part should be renamed away");
    Check(FileDownloadStateStore.Load(item.Id) is null, "resume state should be deleted after completion");
    Check(await ShaFile(item.OutputPath) == Sha(data), "SHA-256 mismatch");

    var starts = server.RequestsFor("/full.bin").Where(r => r.Status == 206 && r.RangeEnd != 0).Select(r => r.RangeStart).Distinct().Count();
    Check(starts >= 8, $"expected 8 parallel ranges, saw {starts}");

    await FileChecksumService.ApplySha256Async(item);
    Check(item.Sha256 == Sha(data), $"DownloadItem.Sha256 '{item.Sha256}'");
    Check(item.DiagnosticsLog.Contains("SHA-256: " + Sha(data)), "SHA-256 diagnostic line missing");
    Check(item.Segments.Count > 0, "Segments should be reported");
});

await Run("work stealing splits a slow connection's range", async () =>
{
    var data = MakeData(16 * MB, seed: 3);
    server.Files["/steal.bin"] = data;
    // 4 connections → initial ranges start at 0, 4, 8, 12 MB. The one at 12 MB trickles at 1 MB/s;
    // split-off requests start elsewhere and run at full speed.
    server.Throttle = (path, start) => path == "/steal.bin" && start == 12L * MB ? 1 * MB : 0;
    try
    {
        var item = NewItem(server.Url("/steal.bin"), connections: 4);
        var clock = Stopwatch.StartNew();
        await service.DownloadAsync(item, 4, null, null, CancellationToken.None);
        clock.Stop();

        Check(await ShaFile(item.OutputPath) == Sha(data), "SHA-256 mismatch after splits");
        var initial = new long?[] { 0, 4L * MB, 8L * MB, 12L * MB };
        var stolen = server.RequestsFor("/steal.bin")
            .Where(r => r.Status == 206 && r.RangeEnd != 0 && !initial.Contains(r.RangeStart))
            .ToList();
        Check(stolen.Count > 0, "no split-off range requests were made");
        Check(stolen.Any(r => r.RangeStart > 12L * MB), "the slow segment was never split");
        Check(item.DiagnosticsLog.Contains("Dynamic segmentation"), "split diagnostic missing");
        Check(clock.Elapsed < TimeSpan.FromSeconds(3.5), $"took {clock.Elapsed.TotalSeconds:0.0}s; the slow 4 MB range alone needs 4s");
        Console.WriteLine($"    {stolen.Count} stolen range(s), {clock.Elapsed.TotalSeconds:0.00}s");
    }
    finally
    {
        server.Throttle = (_, _) => 0;
    }
});

await Run("resume after the process is killed mid-download", async () =>
{
    var data = MakeData(12 * MB + 5, seed: 4);
    server.Files["/crash.bin"] = data;
    server.Throttle = (path, _) => path == "/crash.bin" ? 768 * 1024 : 0;
    var id = Guid.NewGuid().ToString("N");
    var folder = Path.Combine(saveDir, "crash");
    Directory.CreateDirectory(folder);

    using (var child = StartChild(server.Url("/crash.bin"), id, folder))
    {
        var statePath = FileDownloadStateStore.GetStatePath(id);
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            if (child.HasExited)
                throw new Exception($"child exited early (code {child.ExitCode})");
            if (deadline.Elapsed > TimeSpan.FromSeconds(30))
                throw new Exception("child made no progress in 30s");

            var saved = TryLoad(id);
            if (saved is not null && saved.Segments.Sum(s => s.Downloaded) >= data.Length / 4)
                break;
            await Task.Delay(100);
        }

        child.Kill(entireProcessTree: true);
        await child.WaitForExitAsync();
        Check(File.Exists(statePath), "state file should survive the kill");
    }

    var state = TryLoad(id) ?? throw new Exception("state unreadable after kill");
    var done = state.Segments.Sum(s => s.Downloaded);
    Check(done > 0 && done < data.Length, $"saved progress {done}/{data.Length}");
    Check(File.Exists(state.PartPath), ".part should survive the kill");

    // Every byte the state claims is downloaded must already be correct on disk.
    var part = await File.ReadAllBytesAsync(state.PartPath);
    foreach (var segment in state.Segments.Where(s => s.Downloaded > 0))
    {
        var span = (int)segment.Downloaded;
        Check(part.AsSpan((int)segment.Start, span).SequenceEqual(data.AsSpan((int)segment.Start, span)),
            $"saved bytes of segment @{segment.Start} do not match the source");
    }

    server.Throttle = (_, _) => 0;
    var sentBefore = server.BytesSent;
    var item = NewItem(server.Url("/crash.bin"), connections: 4, id: id, folder: folder);
    await service.DownloadAsync(item, 4, null, null, CancellationToken.None);
    var resumedBytes = server.BytesSent - sentBefore;

    Check(await ShaFile(item.OutputPath) == Sha(data), "SHA-256 mismatch after crash resume");
    Check(resumedBytes < data.Length - done / 2, $"resume re-downloaded {resumedBytes} bytes (saved {done})");
    Check(item.DiagnosticsLog.Contains("Resuming download"), "should resume, not restart");
    Console.WriteLine($"    killed at {done * 100 / data.Length}% · resumed {resumedBytes / 1024} KB");
});

await Run("403 on resume → link expired → refresh address completes", async () =>
{
    var data = MakeData(8 * MB + 99, seed: 5);
    server.Files["/expiring.bin"] = data;
    server.Throttle = (path, _) => path == "/expiring.bin" ? 1 * MB : 0;
    var item = NewItem(server.Url("/expiring.bin"), connections: 4);

    using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1200)))
    {
        try
        {
            await service.DownloadAsync(item, 4, null, null, cts.Token);
            throw new Exception("download should have been cancelled");
        }
        catch (OperationCanceledException)
        {
        }
    }

    server.Throttle = (_, _) => 0;
    var partPath = item.PartFilePath;
    var savedBefore = FileDownloadStateStore.Load(item.Id)?.Segments.Sum(s => s.Downloaded) ?? 0;
    Check(savedBefore > 0, "cancel should leave saved progress");

    server.Forbidden["/expiring.bin"] = true;
    try
    {
        await service.DownloadAsync(item, 4, null, null, CancellationToken.None);
        throw new Exception("expected DownloadLinkExpiredException");
    }
    catch (DownloadLinkExpiredException ex)
    {
        Check((int)ex.StatusCode == 403, $"status {(int)ex.StatusCode}");
    }

    Check(File.Exists(partPath), ".part must be kept when the link expires");
    var afterExpiry = FileDownloadStateStore.Load(item.Id);
    Check(afterExpiry is not null && afterExpiry.Segments.Sum(s => s.Downloaded) >= savedBefore, "state must be kept when the link expires");

    // What DownloadManager does: mark failed/expired; the browser then catches the same file again.
    item.Status = DownloadStatus.Failed;
    item.IsLinkExpired = true;
    Check(DownloadAddressRefresh.IsCandidate(item, "expiring.bin", data.Length), "should match by name + size");
    Check(DownloadAddressRefresh.IsCandidate(item, @"C:\Users\x\Downloads\expiring (2).bin", data.Length), "should match a browser path with (n) suffix");
    Check(!DownloadAddressRefresh.IsCandidate(item, "expiring.bin", data.Length + 1), "must not match a different size");
    Check(!DownloadAddressRefresh.IsCandidate(item, "other.bin", data.Length), "must not match a different name");

    // A refresh that points at a different file must fail without touching the downloaded data.
    server.Files["/wrong.bin"] = MakeData(3 * MB, seed: 9);
    DownloadAddressRefresh.Apply(item, server.Url("/wrong.bin"), null);
    try
    {
        await service.DownloadAsync(item, 4, null, null, CancellationToken.None);
        throw new Exception("a different-size file must be rejected");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("different file"))
    {
    }

    Check(File.Exists(partPath), ".part must survive a wrong refresh");

    // The real refresh: same bytes on a new signed URL that needs the new cookie.
    server.Files["/fresh.bin"] = data;
    server.RequiredCookie["/fresh.bin"] = "session=new";
    var headers = new Dictionary<string, string> { ["Cookie"] = "session=new", ["Referer"] = "https://example.test/" };
    DownloadAddressRefresh.Apply(item, server.Url("/fresh.bin"), headers);
    Check(!item.IsLinkExpired, "Apply should clear IsLinkExpired");

    var sentBefore = server.BytesSent;
    await service.DownloadAsync(item, 4, null, null, CancellationToken.None);
    var resumed = server.BytesSent - sentBefore;

    Check(await ShaFile(item.OutputPath) == Sha(data), "SHA-256 mismatch after refresh");
    Check(Path.GetFileName(item.OutputPath) == "expiring.bin", $"kept output name, got {Path.GetFileName(item.OutputPath)}");
    Check(resumed <= data.Length - savedBefore + MB, $"refresh re-downloaded {resumed} bytes (had {savedBefore})");
    Check(server.RequestsFor("/fresh.bin").All(r => r.Status == 206 && r.Cookie == "session=new"), "new cookie must be sent on the refreshed link");
});

await Run("global speed limiter caps all connections", async () =>
{
    var data = MakeData(3 * MB, seed: 6);
    server.Files["/limited.bin"] = data;
    SpeedLimiter.Global.BytesPerSecond = 1 * MB;
    try
    {
        var item = NewItem(server.Url("/limited.bin"), connections: 4);
        var clock = Stopwatch.StartNew();
        await service.DownloadAsync(item, 4, null, null, CancellationToken.None);
        clock.Stop();
        Check(await ShaFile(item.OutputPath) == Sha(data), "SHA-256 mismatch with limiter");
        // 3 MB at 1 MB/s with a 256 KB burst: ~2.75 s.
        Check(clock.Elapsed >= TimeSpan.FromSeconds(2.2), $"too fast for 1 MB/s: {clock.Elapsed.TotalSeconds:0.00}s");
        Check(clock.Elapsed <= TimeSpan.FromSeconds(6), $"too slow for 1 MB/s: {clock.Elapsed.TotalSeconds:0.00}s");
        Console.WriteLine($"    3 MB in {clock.Elapsed.TotalSeconds:0.00}s at 1 MB/s");
    }
    finally
    {
        SpeedLimiter.Global.BytesPerSecond = 0;
    }

    // Changing the limit at runtime applies to callers already waiting.
    var limiter = new SpeedLimiter { BytesPerSecond = 64 * 1024 };
    var waits = Stopwatch.StartNew();
    var slow = Task.Run(async () =>
    {
        for (var i = 0; i < 64; i++)
            await limiter.WaitAsync(16 * 1024, CancellationToken.None);
    });
    await Task.Delay(300);
    limiter.BytesPerSecond = 0;
    await slow;
    Check(waits.Elapsed < TimeSpan.FromSeconds(3), $"lifting the limit did not release waiters ({waits.Elapsed.TotalSeconds:0.0}s)");
});

await Run("server without Range support downloads on one connection", async () =>
{
    var data = MakeData(2 * MB + 1, seed: 7);
    server.Files["/norange.bin"] = data;
    server.NoRanges["/norange.bin"] = true;
    var item = NewItem(server.Url("/norange.bin"), connections: 8);
    await service.DownloadAsync(item, 8, null, null, CancellationToken.None);
    Check(await ShaFile(item.OutputPath) == Sha(data), "SHA-256 mismatch without ranges");
    Check(server.RequestsFor("/norange.bin").Count() == 2, "expected probe + one full GET");
});

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failures.Count} failed");
foreach (var failure in failures)
    Console.WriteLine("FAIL: " + failure);

if (failures.Count == 0)
{
    try { Directory.Delete(runDir, recursive: true); } catch { /* leave it for inspection */ }
}

return failures.Count == 0 ? 0 : 1;

// ---------------------------------------------------------------------------------------------

async Task Run(string name, Func<Task> test)
{
    Console.WriteLine($"- {name}");
    var before = failures.Count;
    try
    {
        await test();
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
    }

    if (failures.Count == before)
    {
        passed++;
        Console.WriteLine("    ok");
    }
    else
    {
        Console.WriteLine("    FAILED: " + failures[^1]);
    }
}

void Check(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

DownloadItem NewItem(string url, int connections, string? id = null, string? folder = null)
{
    var target = folder ?? Path.Combine(saveDir, Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(target);
    return new DownloadItem
    {
        Id = id ?? Guid.NewGuid().ToString("N"),
        Url = url,
        Kind = DownloadKind.DirectFile,
        SaveFolder = target,
        ConnectionCount = connections
    };
}

Process StartChild(string url, string id, string folder)
{
    var info = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };
    var host = Environment.ProcessPath ?? "dotnet";
    info.FileName = host;
    if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        info.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
    foreach (var arg in new[] { "--child", url, id, folder })
        info.ArgumentList.Add(arg);
    info.Environment[AppPaths.DataDirVariable] = dataDir;
    return Process.Start(info) ?? throw new Exception("could not start child process");
}

static FileDownloadState? TryLoad(string id)
{
    try { return FileDownloadStateStore.Load(id); }
    catch (IOException) { return null; }
}

static async Task<int> RunChildAsync(string[] childArgs)
{
    // Child: download until the parent kills it (crash-safe resume test).
    var item = new DownloadItem
    {
        Id = childArgs[2],
        Url = childArgs[1],
        Kind = DownloadKind.DirectFile,
        SaveFolder = childArgs[3],
        ConnectionCount = 4
    };
    await new HttpFileDownloadService().DownloadAsync(item, 4, null, null, CancellationToken.None);
    return 3; // finishing means the parent never killed us
}

static byte[] MakeData(int length, int seed)
{
    var data = new byte[length];
    new Random(seed).NextBytes(data);
    return data;
}

static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

static async Task<string> ShaFile(string path) => await FileChecksumService.ComputeSha256Async(path);

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
