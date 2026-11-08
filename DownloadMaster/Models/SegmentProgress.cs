namespace DownloadMaster.Models;

/// <summary>
/// One connection's slice of a direct-file download, as fractions (0..1) of the whole file.
/// The segment covers [<see cref="Start"/>, <see cref="End"/>); the filled part is
/// [<see cref="Start"/>, <see cref="Start"/> + <see cref="Downloaded"/>].
/// </summary>
public readonly record struct SegmentProgress(double Start, double End, double Downloaded)
{
    /// <summary>Fraction of this segment that is done (0..1).</summary>
    public double Completion => End > Start ? Math.Clamp(Downloaded / (End - Start), 0, 1) : 1;
}
