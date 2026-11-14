// Headless regressions for DownloadMaster's direct-file engine (HttpFileDownloadService):
// integrity, multi-connection + work stealing, crash-safe resume (real process kill), 403 →
// "link expired" → refresh address, the global speed limiter and 206 probe sizing; plus the
// after-download steps: archive extraction (zip-slip, multi-part sets) and antivirus exit codes.
// Exit code 0 = all checks passed, 1 = a check failed (CI friendly).
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
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

// ---- After download: archive extraction and antivirus result mapping (no network) ----------

var archiveRoot = Path.Combine(runDir, "archives");

await Run("zip extracts into a folder named after the archive", async () =>
{
    var dir = Path.Combine(archiveRoot, "zip", "a", "b");
    Directory.CreateDirectory(dir);
    var zip = Path.Combine(dir, "sample.zip");
    WriteZip(zip, ("readme.txt", "hello"), ("docs/", null), ("docs/inner/deep.txt", "deep"));

    var first = await ArchiveExtractor.ExtractAsync(zip, deleteArchive: false);
    Check(first.Outcome == ArchiveExtractOutcome.Extracted, $"outcome {first.Outcome} {first.Detail}");
    Check(first.TargetFolder == Path.Combine(dir, "sample"), $"target '{first.TargetFolder}'");
    Check(File.ReadAllText(Path.Combine(dir, "sample", "readme.txt")) == "hello", "readme.txt content");
    Check(File.ReadAllText(Path.Combine(dir, "sample", "docs", "inner", "deep.txt")) == "deep", "nested file content");
    Check(File.Exists(zip), "archive must be kept when delete is off");

    // The folder name is taken: next one is "sample (2)"; a file called "sample (3)" is skipped too.
    File.WriteAllText(Path.Combine(dir, "sample (3)"), "not a folder");
    var second = await ArchiveExtractor.ExtractAsync(zip, deleteArchive: false);
    Check(second.TargetFolder == Path.Combine(dir, "sample (2)"), $"second target '{second.TargetFolder}'");
    var third = await ArchiveExtractor.ExtractAsync(zip, deleteArchive: true);
    Check(third.TargetFolder == Path.Combine(dir, "sample (4)"), $"third target '{third.TargetFolder}'");
    Check(!File.Exists(zip), "archive should be deleted after extraction when asked");
    Check(third.DeletedFiles is { Count: 1 }, "deleted file list");
});

await Run("zip-slip and absolute entries are rejected", async () =>
{
    // Deep enough that "../../evil.txt" would still land inside this run's folder.
    var dir = Path.Combine(archiveRoot, "slip", "x", "y");
    Directory.CreateDirectory(dir);
    var zip = Path.Combine(dir, "bad.zip");
    WriteZip(zip, ("ok.txt", "fine"), ("../../evil.txt", "escaped"));

    var result = await ArchiveExtractor.ExtractAsync(zip, deleteArchive: true);
    Check(result.Outcome == ArchiveExtractOutcome.Failed, $"outcome {result.Outcome}");
    Check(result.Detail.Contains("Unsafe path"), $"detail '{result.Detail}'");
    Check(!File.Exists(Path.Combine(archiveRoot, "slip", "evil.txt")) && !File.Exists(Path.Combine(dir, "evil.txt")),
        "the escaping entry must not be written");
    Check(!Directory.Exists(Path.Combine(dir, "bad")), "the partial target folder should be removed");
    Check(File.Exists(zip), "a rejected archive must never be deleted");

    var target = Path.Combine(dir, "target");
    foreach (var name in new[] { "../evil.txt", "a/../../evil.txt", @"..\evil.txt", "/etc/evil", @"\evil.txt", "C:/evil.txt", "C:evil.txt" })
    {
        var rejected = false;
        try { ArchiveExtractor.ResolveEntryPath(target, name); }
        catch (UnsafeArchiveEntryException) { rejected = true; }
        if (OperatingSystem.IsWindows() || !name.Contains(':'))
            Check(rejected, $"'{name}' should be rejected");
    }

    Check(ArchiveExtractor.ResolveEntryPath(target, "sub/./file.txt") == Path.Combine(Path.GetFullPath(target), "sub", "file.txt"), "normal entry path");

    // Same rules for tar: a "../" entry fails the whole archive.
    var tgz = Path.Combine(dir, "bad.tar.gz");
    WriteTarGz(tgz, ("good.txt", "ok"), ("../../evil-tar.txt", "escaped"));
    var tarResult = await ArchiveExtractor.ExtractAsync(tgz, deleteArchive: false);
    Check(tarResult.Outcome == ArchiveExtractOutcome.Failed, $"tar outcome {tarResult.Outcome}");
    Check(!File.Exists(Path.Combine(archiveRoot, "slip", "evil-tar.txt")), "tar entry escaped");
});

await Run("tar.gz extracts in-process", async () =>
{
    var dir = Path.Combine(archiveRoot, "tgz");
    Directory.CreateDirectory(dir);
    var tgz = Path.Combine(dir, "bundle.tar.gz");
    WriteTarGz(tgz, ("bundle/a.txt", "A"), ("bundle/sub/b.txt", "B"));
    var result = await ArchiveExtractor.ExtractAsync(tgz, deleteArchive: false);
    Check(result.Outcome == ArchiveExtractOutcome.Extracted, $"outcome {result.Outcome} {result.Detail}");
    Check(result.TargetFolder == Path.Combine(dir, "bundle"), $"target '{result.TargetFolder}' (both .tar and .gz stripped)");
    Check(File.ReadAllText(Path.Combine(dir, "bundle", "bundle", "sub", "b.txt")) == "B", "nested tar file");
});

await Run("multi-part sets: only the first part, only when complete", async () =>
{
    var dir = Path.Combine(archiveRoot, "multi");
    Directory.CreateDirectory(dir);
    string P(string name) => Path.Combine(dir, name);

    var cases = new (string Name, bool Multi, bool First, string FirstPart, string BaseName)[]
    {
        ("movie.part1.rar", true, true, "movie.part1.rar", "movie"),
        ("movie.part01.rar", true, true, "movie.part01.rar", "movie"),
        ("movie.part02.rar", true, false, "movie.part01.rar", "movie"),
        ("movie.part10.rar", true, false, "movie.part01.rar", "movie"),
        ("old.r00", true, false, "old.rar", "old"),
        ("old.r12", true, false, "old.rar", "old"),
        ("disk.7z.001", true, true, "disk.7z.001", "disk"),
        ("disk.7z.002", true, false, "disk.7z.001", "disk"),
        ("pack.zip.001", true, true, "pack.zip.001", "pack"),
        ("pack.zip.003", true, false, "pack.zip.001", "pack"),
        ("single.rar", false, true, "single.rar", "single"),
        ("plain.zip", false, true, "plain.zip", "plain"),
        ("plain.tgz", false, true, "plain.tgz", "plain"),
        ("plain.tar.xz", false, true, "plain.tar.xz", "plain"),
    };
    foreach (var c in cases)
    {
        var info = ArchiveExtractor.Identify(P(c.Name)) ?? throw new Exception($"{c.Name} not recognised");
        Check(info.IsMultiPart == c.Multi, $"{c.Name}: multi {info.IsMultiPart}");
        Check(info.IsFirstPart == c.First, $"{c.Name}: first {info.IsFirstPart}");
        Check(info.FirstPartPath == P(c.FirstPart), $"{c.Name}: first part {info.FirstPartPath}");
        Check(info.BaseName == c.BaseName, $"{c.Name}: base {info.BaseName}");
    }

    Check(ArchiveExtractor.Identify(P("notes.txt")) is null, "a .txt is not an archive");
    Check(ArchiveExtractor.Identify(P("report.part.pdf")) is null, "report.part.pdf is not an archive");
    Check(ArchiveExtractor.Identify(P("plain.zip"))!.Format == ArchiveFormat.Zip, "zip format");
    Check(ArchiveExtractor.Identify(P("pack.zip.001"))!.Format == ArchiveFormat.SevenZip, ".zip.001 goes to 7-Zip");

    // name.rar becomes multi-part once a name.r00 sibling exists.
    File.WriteAllText(P("old.rar"), "x");
    Check(!ArchiveExtractor.Identify(P("old.rar"))!.IsMultiPart, "old.rar alone is single");
    File.WriteAllText(P("old.r00"), "x");
    var old = ArchiveExtractor.Identify(P("old.rar"))!;
    Check(old.IsMultiPart && old.IsFirstPart, "old.rar + old.r00 is the first part of a set");
    Check(ArchiveExtractor.IsSetComplete(old), "old.rar + r00 is complete");
    File.WriteAllText(P("old.r02"), "x");
    Check(!ArchiveExtractor.IsSetComplete(old), "gap r00 → r02 is incomplete");
    File.WriteAllText(P("old.r01"), "x");
    Check(ArchiveExtractor.IsSetComplete(old), "r00..r02 complete");
    File.WriteAllText(P("old.r03.part"), "x");
    Check(!ArchiveExtractor.IsSetComplete(old), "old.r03 still downloading (.part)");

    // name.partN.rar: wait for every part, skip later parts silently.
    File.WriteAllText(P("show.part1.rar"), "x");
    File.WriteAllText(P("show.part2.rar"), "x");
    File.WriteAllText(P("show.part3.rar.part"), "x");
    var show = ArchiveExtractor.Identify(P("show.part2.rar"))!;
    Check(!ArchiveExtractor.IsSetComplete(show), "show.part3 is still a .part download");
    var later = await ArchiveExtractor.ExtractAsync(P("show.part2.rar"), deleteArchive: false);
    Check(later.Outcome == ArchiveExtractOutcome.LaterPart, $"later part outcome {later.Outcome}");
    var firstPart = await ArchiveExtractor.ExtractAsync(P("show.part1.rar"), deleteArchive: false);
    Check(firstPart.Outcome == ArchiveExtractOutcome.WaitingForParts, $"first part outcome {firstPart.Outcome}");
    Check(!Directory.Exists(P("show")), "nothing may be extracted while parts are missing");

    File.Delete(P("show.part3.rar.part"));
    Check(ArchiveExtractor.IsSetComplete(show), "part1 + part2 complete");
    Check(!ArchiveExtractor.IsSetComplete(show, [P("show.part3.rar")]), "a queued part3 keeps the set waiting");
    Check(ArchiveExtractor.IsSetComplete(show, [P("other.part3.rar"), P("show.part1.rar")]), "unrelated pending files do not count");
    File.WriteAllText(P("show.part4.rar"), "x");
    Check(!ArchiveExtractor.IsSetComplete(show), "gap part2 → part4 is incomplete");

    var split = ArchiveExtractor.Identify(P("disk.7z.002"))!;
    File.WriteAllText(P("disk.7z.002"), "x");
    Check(!ArchiveExtractor.IsSetComplete(split), "disk.7z.001 missing");
    File.WriteAllText(P("disk.7z.001"), "x");
    Check(ArchiveExtractor.IsSetComplete(split), "001 + 002 complete");
    Check(ArchiveExtractor.GetSetParts(split).Count == 2, "two parts on disk");
});

await Run("7-Zip formats (.7z, .tar.xz) when 7-Zip is installed", async () =>
{
    var sevenZip = ArchiveExtractor.FindSevenZip();
    if (sevenZip is null)
    {
        Console.WriteLine("    skipped: 7-Zip not found");
        return;
    }

    var dir = Path.Combine(archiveRoot, "sevenzip");
    var source = Path.Combine(dir, "source");
    Directory.CreateDirectory(Path.Combine(source, "inner"));
    File.WriteAllText(Path.Combine(source, "inner", "x.txt"), "seven");

    var archive = Path.Combine(dir, "made.7z");
    var made = await ExternalToolRunner.RunAsync(sevenZip, ["a", "-bd", archive, Path.Combine(source, "inner")], TimeSpan.FromMinutes(1));
    Check(made.ExitCode == 0, $"7z a failed: {made.Output}");
    var result = await ArchiveExtractor.ExtractAsync(archive, deleteArchive: true);
    Check(result.Outcome == ArchiveExtractOutcome.Extracted, $"7z outcome {result.Outcome} {result.Detail}");
    Check(File.ReadAllText(Path.Combine(dir, "made", "inner", "x.txt")) == "seven", "7z content");
    Check(!File.Exists(archive), "7z archive deleted");

    var tar = Path.Combine(dir, "tarred.tar");
    WriteTar(tar, ("t/y.txt", "xz"));
    var xz = Path.Combine(dir, "tarred.tar.xz");
    made = await ExternalToolRunner.RunAsync(sevenZip, ["a", "-bd", "-txz", xz, tar], TimeSpan.FromMinutes(1));
    Check(made.ExitCode == 0, $"7z a -txz failed: {made.Output}");
    File.Delete(tar);
    result = await ArchiveExtractor.ExtractAsync(xz, deleteArchive: false);
    Check(result.Outcome == ArchiveExtractOutcome.Extracted, $"tar.xz outcome {result.Outcome} {result.Detail}");
    Check(File.ReadAllText(Path.Combine(dir, "tarred", "t", "y.txt")) == "xz", "tar.xz content (not a bare .tar)");

    // A password prompt must fail fast (stdin is closed), not hang the extraction queue.
    var locked = Path.Combine(dir, "locked.7z");
    made = await ExternalToolRunner.RunAsync(sevenZip, ["a", "-bd", "-psecret", locked, Path.Combine(source, "inner")], TimeSpan.FromMinutes(1));
    Check(made.ExitCode == 0, $"7z a -p failed: {made.Output}");
    var clock = Stopwatch.StartNew();
    result = await ArchiveExtractor.ExtractAsync(locked, deleteArchive: true);
    Check(result.Outcome == ArchiveExtractOutcome.Failed && result.Detail.Contains("password"), $"locked outcome {result.Outcome} '{result.Detail}'");
    Check(clock.Elapsed < TimeSpan.FromSeconds(30), $"password prompt took {clock.Elapsed.TotalSeconds:0}s");
    Check(File.Exists(locked) && !Directory.Exists(Path.Combine(dir, "locked")), "failed extraction keeps the archive and removes the folder");
    Console.WriteLine($"    7-Zip: {sevenZip}");
});

await Run("antivirus exit codes map to clean / threat / could not scan", () =>
{
    var defender = AntivirusScanner.DefenderName;
    var clam = AntivirusScanner.ClamAvName;
    Check(AntivirusScanner.Interpret(defender, new(0, "", false)).Outcome == ScanOutcome.Clean, "Defender 0");
    var threat = AntivirusScanner.Interpret(defender, new(2, "Scan starting...\nThreat                  : Virus:DOS/EICAR_Test_File\n", false));
    Check(threat.Outcome == ScanOutcome.ThreatFound && threat.Detail == "Virus:DOS/EICAR_Test_File", $"Defender 2 → {threat}");
    Check(AntivirusScanner.Interpret(defender, new(1, "", false)).Outcome == ScanOutcome.Failed, "Defender 1 is not a threat");
    // Real output of MpCmdRun with Defender turned off: exit code 2, but no scan happened.
    var disabled = AntivirusScanner.Interpret(defender, new(2, "CmdTool: Failed with hr = 0x80004005. Check C:\\Temp\\MpCmdRun.log for more information", false));
    Check(disabled.Outcome == ScanOutcome.Failed && disabled.Detail.Contains("0x80004005"), $"Defender off → {disabled}");
    Check(AntivirusScanner.Interpret(defender, new(-2147024891, "Access denied", false)).Outcome == ScanOutcome.Failed, "Defender error");
    var clamThreat = AntivirusScanner.Interpret(clam, new(1, "/tmp/eicar.com: Win.Test.EICAR_HDB-1 FOUND\n", false));
    Check(clamThreat.Outcome == ScanOutcome.ThreatFound && clamThreat.Detail == "Win.Test.EICAR_HDB-1", $"clamscan 1 → {clamThreat}");
    Check(AntivirusScanner.Interpret(clam, new(2, "", false)).Outcome == ScanOutcome.Failed, "clamscan 2 is an error");
    Check(AntivirusScanner.Interpret(clam, new(-1, "", true)).Outcome == ScanOutcome.Failed, "timeout");
    return Task.CompletedTask;
});

// Opt-in (runs the real scanner on one small file in test-output): DOWNLOADMASTER_TEST_ANTIVIRUS=1
if (Environment.GetEnvironmentVariable("DOWNLOADMASTER_TEST_ANTIVIRUS") == "1")
{
    await Run("real antivirus scan of a small clean file", async () =>
    {
        var file = Path.Combine(archiveRoot, "scan", "clean.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "just text");
        var result = await AntivirusScanner.ScanAsync(file);
        Console.WriteLine($"    {result.Scanner}: {result.Outcome} {result.Detail}");
        Check(result.Outcome != ScanOutcome.ThreatFound, "a text file must not be reported as a threat");
        Check(File.Exists(file), "the scanned file must be kept");
    });
}

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

// Entries with a null text are folders.
static void WriteZip(string path, params (string Name, string? Text)[] entries)
{
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var (name, text) in entries)
    {
        var entry = zip.CreateEntry(name);
        if (text is null)
            continue;
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }
}

static void WriteTar(string path, params (string Name, string Text)[] entries)
{
    using var file = File.Create(path);
    WriteTarTo(file, entries);
}

static void WriteTarGz(string path, params (string Name, string Text)[] entries)
{
    using var file = File.Create(path);
    using var gzip = new GZipStream(file, CompressionLevel.Fastest);
    WriteTarTo(gzip, entries);
}

static void WriteTarTo(Stream stream, (string Name, string Text)[] entries)
{
    using var writer = new TarWriter(stream, TarEntryFormat.Pax, leaveOpen: true);
    foreach (var (name, text) in entries)
    {
        var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text))
        };
        writer.WriteEntry(entry);
    }
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
