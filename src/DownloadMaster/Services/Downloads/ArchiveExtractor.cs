using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace DownloadMaster.Services;

public enum ArchiveFormat
{
    /// <summary>.zip — System.IO.Compression.</summary>
    Zip,
    /// <summary>.tar — System.Formats.Tar.</summary>
    Tar,
    /// <summary>.tar.gz / .tgz — GZipStream + System.Formats.Tar.</summary>
    TarGz,
    /// <summary>.tar.xz / .txz — 7-Zip decompresses to stdout, System.Formats.Tar unpacks.</summary>
    TarXz,
    /// <summary>.7z, .rar, split .7z.001 / .zip.001 / .rar.001 — extracted by 7-Zip.</summary>
    SevenZip
}

public enum ArchiveExtractOutcome
{
    NotAnArchive,
    /// <summary>A later part of a multi-part set whose set cannot be extracted yet (skipped silently).</summary>
    LaterPart,
    /// <summary>The first part of a multi-part set, other parts still missing or downloading.</summary>
    WaitingForParts,
    /// <summary>The set is already being extracted (another part finished at the same time).</summary>
    AlreadyRunning,
    Extracted,
    NeedsSevenZip,
    Failed
}

/// <summary>What a file name says about an archive.</summary>
/// <param name="Path">Full path of the file that was looked at.</param>
/// <param name="FirstPartPath">Full path of the part to extract (the file itself for single archives).</param>
/// <param name="BaseName">Name of the folder to extract into (archive name without archive extensions).</param>
/// <param name="IsMultiPart">The file belongs to a numbered multi-part set.</param>
/// <param name="IsFirstPart">This file is the one to extract (always true for single archives).</param>
public sealed record ArchiveInfo(
    string Path,
    ArchiveFormat Format,
    string FirstPartPath,
    string BaseName,
    bool IsMultiPart,
    bool IsFirstPart);

public sealed record ArchiveExtractResult(
    ArchiveExtractOutcome Outcome,
    string FirstPartPath = "",
    string TargetFolder = "",
    string Detail = "",
    IReadOnlyList<string>? DeletedFiles = null);

/// <summary>An archive entry that would land outside the extraction folder (zip-slip) or uses an absolute path.</summary>
public sealed class UnsafeArchiveEntryException(string entryName)
    : IOException($"Unsafe path in archive: {entryName}")
{
    public string EntryName { get; } = entryName;
}

/// <summary>
/// "Extract archives when done": unpacks a finished archive into a new folder named after it, next
/// to it. Zip and tar(.gz) are handled in-process; 7z, rar, split archives and .tar.xz use 7-Zip when
/// it is installed. Multi-part sets are extracted once, from the first part, when every part is on disk.
/// UI-free (no settings or localization) so the regression tests can link it.
/// </summary>
public static partial class ArchiveExtractor
{
    // Unpacking is disk-bound: one archive at a time.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    // First-part paths being extracted now, and sets already extracted in this session.
    private static readonly ConcurrentDictionary<string, byte> Running = new(PathComparer);
    private static readonly ConcurrentDictionary<string, byte> Extracted = new(PathComparer);

    private static StringComparer PathComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    // name.part1.rar / name.part01.rar
    [GeneratedRegex(@"^(?<base>.+)\.part(?<n>\d{1,4})\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RarPartRegex();

    // name.r00, name.r01 ... (old-style volumes after name.rar)
    [GeneratedRegex(@"^(?<base>.+)\.r(?<n>\d{2,3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RarOldVolumeRegex();

    // name.7z.001, name.zip.001, name.rar.001, name.tar.001
    [GeneratedRegex(@"^(?<base>.+)\.(?<ext>7z|zip|rar|tar)\.(?<n>\d{3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SplitRegex();

    /// <summary>Classifies a file by name. Null when it is not a supported archive.</summary>
    public static ArchiveInfo? Identify(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var dir = System.IO.Path.GetDirectoryName(full) ?? string.Empty;
        var name = System.IO.Path.GetFileName(full);

        var match = RarPartRegex().Match(name);
        if (match.Success)
        {
            var digits = match.Groups["n"].Value;
            var baseName = match.Groups["base"].Value;
            var first = System.IO.Path.Combine(dir, $"{baseName}.part{"1".PadLeft(digits.Length, '0')}.rar");
            return new ArchiveInfo(full, ArchiveFormat.SevenZip, first, baseName, true, int.Parse(digits) == 1);
        }

        match = SplitRegex().Match(name);
        if (match.Success)
        {
            var baseName = match.Groups["base"].Value;
            var ext = match.Groups["ext"].Value;
            var first = System.IO.Path.Combine(dir, $"{baseName}.{ext}.001");
            return new ArchiveInfo(full, ArchiveFormat.SevenZip, first, baseName, true, int.Parse(match.Groups["n"].Value) == 1);
        }

        match = RarOldVolumeRegex().Match(name);
        if (match.Success)
        {
            var baseName = match.Groups["base"].Value;
            var first = System.IO.Path.Combine(dir, baseName + ".rar");
            return new ArchiveInfo(full, ArchiveFormat.SevenZip, first, baseName, true, false);
        }

        if (TryStrip(name, ".rar", out var rarBase))
        {
            var multi = Directory.Exists(dir) && File.Exists(System.IO.Path.Combine(dir, rarBase + ".r00"));
            return new ArchiveInfo(full, ArchiveFormat.SevenZip, full, rarBase, multi, true);
        }

        if (TryStrip(name, ".7z", out var sevenBase))
            return Single(ArchiveFormat.SevenZip, sevenBase);
        if (TryStrip(name, ".zip", out var zipBase))
            return Single(ArchiveFormat.Zip, zipBase);
        if (TryStrip(name, ".tar.gz", out var tgzBase) || TryStrip(name, ".tgz", out tgzBase))
            return Single(ArchiveFormat.TarGz, tgzBase);
        if (TryStrip(name, ".tar.xz", out var txzBase) || TryStrip(name, ".txz", out txzBase))
            return Single(ArchiveFormat.TarXz, txzBase);
        if (TryStrip(name, ".tar", out var tarBase))
            return Single(ArchiveFormat.Tar, tarBase);

        return null;

        ArchiveInfo Single(ArchiveFormat format, string baseName) =>
            new(full, format, full, baseName, false, true);
    }

    /// <summary>Every part of the set that is on disk (just the archive for single archives).</summary>
    public static IReadOnlyList<string> GetSetParts(ArchiveInfo info)
    {
        if (!info.IsMultiPart)
            return File.Exists(info.FirstPartPath) ? [info.FirstPartPath] : [];

        var dir = System.IO.Path.GetDirectoryName(info.FirstPartPath);
        if (dir is null || !Directory.Exists(dir))
            return [];

        return Directory.EnumerateFiles(dir)
            .Where(file => BelongsToSet(file, info))
            .OrderBy(file => file, PathComparer)
            .ToList();
    }

    /// <summary>
    /// True when the set can be extracted: the first part exists, no part is still a ".part"
    /// download, no part is waiting in <paramref name="pendingPaths"/> (queued in the app) and the
    /// part numbers have no gaps. Single archives only need to exist (a plain name.rar also waits for
    /// name.r00... volumes that are still downloading).
    /// </summary>
    public static bool IsSetComplete(ArchiveInfo info, IEnumerable<string>? pendingPaths = null)
    {
        var first = info.FirstPartPath;
        if (!File.Exists(first))
            return false;

        // name.rar may be the head of old-style volumes (name.r00, name.r01...) that are not on disk yet.
        var oldStyleRar = IsOldStyleRarHead(first);
        if (!info.IsMultiPart && !oldStyleRar)
            return true;

        if (pendingPaths is not null
            && pendingPaths.Any(p => !string.Equals(System.IO.Path.GetFullPath(p), first, PathComparison) && BelongsToSet(p, info)))
            return false;

        var numbers = new List<int>();
        foreach (var file in Directory.EnumerateFiles(System.IO.Path.GetDirectoryName(first)!))
        {
            // The engine downloads into "<name>.part" (not to be confused with the ".partN.rar" naming).
            if (file.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                && BelongsToSet(file[..^".part".Length], info))
                return false;

            if (BelongsToSet(file, info) && PartNumber(file) is { } n)
                numbers.Add(n);
        }

        // name.rar + r00, r01...; name.part1.rar, part2...; name.7z.001, 002...: no gaps.
        numbers.Sort();
        var expected = oldStyleRar ? 0 : 1;
        foreach (var n in numbers)
        {
            if (n != expected)
                return false;
            expected++;
        }

        return true;
    }

    /// <summary>
    /// Extracts the archive at <paramref name="path"/> (or, for a later part, its whole set once it
    /// is complete). <paramref name="onStarted"/> runs once extraction really begins. Never throws
    /// except for cancellation; failures come back as <see cref="ArchiveExtractOutcome.Failed"/>.
    /// </summary>
    public static async Task<ArchiveExtractResult> ExtractAsync(
        string path,
        bool deleteArchive,
        IEnumerable<string>? pendingPaths = null,
        Action<ArchiveInfo>? onStarted = null,
        CancellationToken ct = default)
    {
        var info = Identify(path);
        if (info is null)
            return new ArchiveExtractResult(ArchiveExtractOutcome.NotAnArchive);

        var first = info.FirstPartPath;
        if (!IsSetComplete(info, pendingPaths?.ToList()))
            return new ArchiveExtractResult(
                info.IsFirstPart ? ArchiveExtractOutcome.WaitingForParts : ArchiveExtractOutcome.LaterPart, first);

        // A later part finishing again after its set was extracted must not unpack it twice.
        if (!info.IsFirstPart && Extracted.ContainsKey(first))
            return new ArchiveExtractResult(ArchiveExtractOutcome.LaterPart, first);

        var firstInfo = info.IsFirstPart ? info : Identify(first) ?? info;
        string? sevenZip = null;
        if (firstInfo.Format is ArchiveFormat.SevenZip or ArchiveFormat.TarXz)
        {
            sevenZip = FindSevenZip();
            if (sevenZip is null)
                return new ArchiveExtractResult(ArchiveExtractOutcome.NeedsSevenZip, first);
        }

        if (!Running.TryAdd(first, 0))
            return new ArchiveExtractResult(ArchiveExtractOutcome.AlreadyRunning, first);

        try
        {
            onStarted?.Invoke(firstInfo);
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var parent = System.IO.Path.GetDirectoryName(first)!;
                var target = CreateUniqueFolder(parent, firstInfo.BaseName);
                try
                {
                    await Task.Run(() => ExtractIntoAsync(firstInfo.Format, first, target, sevenZip, ct), ct)
                        .ConfigureAwait(false);
                }
                catch
                {
                    TryDeleteDirectory(target);
                    throw;
                }

                Extracted[first] = 0;
                var deleted = deleteArchive ? DeleteParts(firstInfo) : [];
                return new ArchiveExtractResult(ArchiveExtractOutcome.Extracted, first, target, DeletedFiles: deleted);
            }
            finally
            {
                Gate.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ArchiveExtractResult(ArchiveExtractOutcome.Failed, first, Detail: ex.Message);
        }
        finally
        {
            Running.TryRemove(first, out _);
        }
    }

    /// <summary>Creates and returns a new folder "name", "name (2)", "name (3)"... in <paramref name="parent"/>.</summary>
    public static string CreateUniqueFolder(string parent, string name)
    {
        var safe = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars())).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(safe))
            safe = "archive";

        for (var i = 1; ; i++)
        {
            var candidate = System.IO.Path.Combine(parent, i == 1 ? safe : $"{safe} ({i})");
            if (Directory.Exists(candidate) || File.Exists(candidate))
                continue;

            Directory.CreateDirectory(candidate);
            return candidate;
        }
    }

    /// <summary>
    /// Full destination path of an archive entry inside <paramref name="targetDir"/>; null for an
    /// empty name. Throws <see cref="UnsafeArchiveEntryException"/> for absolute paths, drive or
    /// stream names, and entries that resolve outside the folder ("../../evil").
    /// </summary>
    public static string? ResolveEntryPath(string targetDir, string entryName)
    {
        // Zip names use '/', but "..\..\evil" must not slip through on Windows either.
        var name = entryName.Replace('\\', '/');
        if (name.Length == 0)
            return null;

        if (name.StartsWith('/')
            || System.IO.Path.IsPathRooted(name)
            || (OperatingSystem.IsWindows() && name.Contains(':')))
            throw new UnsafeArchiveEntryException(entryName);

        var root = System.IO.Path.GetFullPath(targetDir);
        var rootWithSeparator = root.EndsWith(System.IO.Path.DirectorySeparatorChar)
            ? root
            : root + System.IO.Path.DirectorySeparatorChar;
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, name));

        if (!full.StartsWith(rootWithSeparator, PathComparison)
            && !string.Equals(full, root, PathComparison))
            throw new UnsafeArchiveEntryException(entryName);

        return full;
    }

    /// <summary>
    /// Extracts a .zip into <paramref name="targetDir"/> after checking every entry; an unsafe entry
    /// rejects the whole archive before anything is written. Returns the number of files written.
    /// </summary>
    public static int ExtractZip(string zipPath, string targetDir, CancellationToken ct = default)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var plan = new List<(ZipArchiveEntry Entry, string Destination, bool IsDirectory)>();
        foreach (var entry in archive.Entries)
        {
            if (ResolveEntryPath(targetDir, entry.FullName) is not { } destination)
                continue;

            var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            plan.Add((entry, destination, isDirectory));
        }

        var files = 0;
        foreach (var (entry, destination, isDirectory) in plan)
        {
            ct.ThrowIfCancellationRequested();
            if (isDirectory)
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
            files++;
        }

        return files;
    }

    /// <summary>
    /// Unpacks a tar stream into <paramref name="targetDir"/>: regular files and folders only
    /// (links, devices and FIFOs are skipped), every path checked like <see cref="ResolveEntryPath"/>.
    /// </summary>
    public static async Task<int> ExtractTarAsync(Stream tar, string targetDir, CancellationToken ct = default)
    {
        await using var reader = new TarReader(tar, leaveOpen: true);
        var files = 0;
        while (await reader.GetNextEntryAsync(copyData: false, ct).ConfigureAwait(false) is { } entry)
        {
            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    if (ResolveEntryPath(targetDir, entry.Name) is { } folder)
                        Directory.CreateDirectory(folder);
                    break;

                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                case TarEntryType.ContiguousFile:
                    if (ResolveEntryPath(targetDir, entry.Name) is not { } destination)
                        break;
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                    await entry.ExtractToFileAsync(destination, overwrite: true, ct).ConfigureAwait(false);
                    files++;
                    break;
            }
        }

        return files;
    }

    /// <summary>7-Zip: 7z / 7zz / 7za on PATH (or Homebrew, /usr/local/bin...), else the usual Windows install folders.</summary>
    public static string? FindSevenZip()
    {
        var onPath = ToolLocator.FindExecutable("7z", "7zz", "7za");
        if (onPath is not null)
            return onPath;

        if (!OperatingSystem.IsWindows())
            return null;

        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(root))
                continue;

            var exe = System.IO.Path.Combine(root, "7-Zip", "7z.exe");
            if (File.Exists(exe))
                return exe;
        }

        return null;
    }

    private static async Task ExtractIntoAsync(ArchiveFormat format, string archive, string target, string? sevenZip, CancellationToken ct)
    {
        switch (format)
        {
            case ArchiveFormat.Zip:
                ExtractZip(archive, target, ct);
                break;

            case ArchiveFormat.Tar:
            {
                await using var file = OpenRead(archive);
                await ExtractTarAsync(file, target, ct).ConfigureAwait(false);
                break;
            }

            case ArchiveFormat.TarGz:
            {
                await using var file = OpenRead(archive);
                await using var gzip = new GZipStream(file, CompressionMode.Decompress);
                await ExtractTarAsync(gzip, target, ct).ConfigureAwait(false);
                break;
            }

            case ArchiveFormat.TarXz:
                await ExtractTarXzAsync(sevenZip!, archive, target, ct).ConfigureAwait(false);
                break;

            default:
                await RunSevenZipAsync(sevenZip!, archive, target, ct).ConfigureAwait(false);
                break;
        }
    }

    private static async Task RunSevenZipAsync(string sevenZip, string archive, string target, CancellationToken ct)
    {
        // x = keep folders, -y = yes to all, -bd = no progress meter, -- = the archive name is never a switch.
        var result = await ExternalToolRunner.RunAsync(
            sevenZip,
            ["x", "-y", "-bd", "-o" + target, "--", archive],
            TimeSpan.FromHours(6),
            ct).ConfigureAwait(false);

        // 0 = OK, 1 = warnings (for example a locked file was skipped); 2+ = errors.
        if (result.TimedOut)
            throw new TimeoutException("7-Zip did not finish.");
        if (result.ExitCode is not (0 or 1))
            throw new IOException(SevenZipError(result));
    }

    private static async Task ExtractTarXzAsync(string sevenZip, string archive, string target, CancellationToken ct)
    {
        // 7-Zip only removes the .xz layer (it would leave a .tar); stream that into the tar reader.
        using var process = System.Diagnostics.Process.Start(
            ExternalToolRunner.CreateStartInfo(sevenZip, ["x", "-so", "-bd", "--", archive]))
            ?? throw new IOException("Could not start 7-Zip.");
        process.StandardInput.Close();
        var stderr = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await ExtractTarAsync(process.StandardOutput.BaseStream, target, ct).ConfigureAwait(false);
            // Drain anything after the tar end marker so 7-Zip can exit.
            await process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            ExternalToolRunner.Kill(process);
            throw;
        }

        if (process.ExitCode is not (0 or 1))
            throw new IOException(SevenZipError(new ExternalToolRunner.Result(process.ExitCode, await stderr.ConfigureAwait(false), false)));
    }

    private static string SevenZipError(ExternalToolRunner.Result result)
    {
        // stdin is closed, so a password prompt ends at once instead of waiting forever.
        if (result.Output.Contains("Enter password", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains("Wrong password", StringComparison.OrdinalIgnoreCase))
            return "the archive is password-protected";

        var line = result.Output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                || l.Contains("Missing volume", StringComparison.OrdinalIgnoreCase))
            ?? ExternalToolRunner.LastLine(result.Output);
        return string.IsNullOrWhiteSpace(line) ? $"7-Zip exit code {result.ExitCode}" : $"7-Zip: {line}";
    }

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static IReadOnlyList<string> DeleteParts(ArchiveInfo info)
    {
        var deleted = new List<string>();
        foreach (var part in GetSetParts(info))
        {
            try
            {
                File.Delete(part);
                deleted.Add(part);
            }
            catch
            {
                // in use or read-only: leave it
            }
        }

        return deleted;
    }

    private static bool BelongsToSet(string file, ArchiveInfo info)
    {
        if (!string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file)),
                System.IO.Path.GetDirectoryName(info.FirstPartPath), PathComparison))
            return false;

        return Identify(file) is { } other && string.Equals(other.FirstPartPath, info.FirstPartPath, PathComparison);
    }

    /// <summary>name.rar (not name.partN.rar): the head of an old-style volume set, or a single rar.</summary>
    private static bool IsOldStyleRarHead(string firstPartPath)
    {
        var name = System.IO.Path.GetFileName(firstPartPath);
        return name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) && !RarPartRegex().IsMatch(name);
    }

    /// <summary>Volume number of a part (name.rar of an old-style set has none).</summary>
    private static int? PartNumber(string file)
    {
        var name = System.IO.Path.GetFileName(file);
        var match = RarPartRegex().Match(name);
        if (match.Success)
            return int.Parse(match.Groups["n"].Value);
        match = SplitRegex().Match(name);
        if (match.Success)
            return int.Parse(match.Groups["n"].Value);
        match = RarOldVolumeRegex().Match(name);
        return match.Success ? int.Parse(match.Groups["n"].Value) : null;
    }

    private static bool TryStrip(string name, string extension, out string baseName)
    {
        if (name.Length > extension.Length && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            baseName = name[..^extension.Length];
            return true;
        }

        baseName = string.Empty;
        return false;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort: a partly extracted folder is left for the user
        }
    }
}
