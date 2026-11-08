using System.IO;
using System.Security.Cryptography;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

/// <summary>Post-download SHA-256, streamed off the UI thread so huge files never block completion.</summary>
public static class FileChecksumService
{
    // One hash at a time: several multi-GB files hashing in parallel would just thrash the disk.
    private static readonly SemaphoreSlim HashGate = new(1, 1);

    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Hashes the item's finished file and stores the result in <see cref="DownloadItem.Sha256"/>
    /// plus a diagnostic line. Never throws (failures are logged to the item's diagnostics).
    /// </summary>
    public static async Task ApplySha256Async(DownloadItem item, CancellationToken ct = default)
    {
        var path = item.OutputPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        await HashGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            item.IsComputingSha256 = true;
            var hash = await Task.Run(() => ComputeSha256Async(path, ct), ct).ConfigureAwait(false);
            item.Sha256 = hash;
            item.AppendDiagnostic($"SHA-256: {hash}");
        }
        catch (OperationCanceledException)
        {
            // app closing or item removed
        }
        catch (Exception ex)
        {
            item.AppendDiagnostic($"SHA-256 failed: {ex.Message}");
        }
        finally
        {
            item.IsComputingSha256 = false;
            HashGate.Release();
        }
    }
}
