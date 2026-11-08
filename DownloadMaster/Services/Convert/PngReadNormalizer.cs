using System.Buffers.Binary;

namespace DownloadMaster.Services;

/// <summary>
/// Some game/export PNGs end with a corrupted trailer chunk type <c>IENX</c>
/// instead of <c>IEND</c>. Pillow still opens them; Magick/libpng reports
/// <c>IENX: CRC error</c> and aborts. Rewrite the trailer before Magick reads.
/// </summary>
internal static class PngReadNormalizer
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] IendType = "IEND"u8.ToArray();
    private static readonly byte[] IenxType = "IENX"u8.ToArray();
    // CRC of type+data for empty IEND (standard PNG trailer).
    private static readonly byte[] IendCrc = [0xAE, 0x42, 0x60, 0x82];

    public static byte[]? TryNormalizeForRead(string path)
    {
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return null;

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 8 + 12 || !bytes.AsSpan(0, 8).SequenceEqual(PngSignature))
            return null;

        var changed = false;
        var offset = 8;
        while (offset + 12 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
            if (offset + 12L + length > bytes.Length)
                break;

            var typeOffset = offset + 4;
            var type = bytes.AsSpan(typeOffset, 4);
            var isIenx = type.SequenceEqual(IenxType);
            var isIend = type.SequenceEqual(IendType);

            if (isIenx || (isIend && length == 0 && !HasValidIendCrc(bytes, offset)))
            {
                // Force a standard empty IEND trailer at this chunk.
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), 0);
                IendType.CopyTo(bytes.AsSpan(typeOffset, 4));
                IendCrc.CopyTo(bytes.AsSpan(offset + 8, 4));
                changed = true;

                // Drop any garbage after the fixed trailer.
                var end = offset + 12;
                if (end < bytes.Length)
                {
                    var trimmed = new byte[end];
                    Buffer.BlockCopy(bytes, 0, trimmed, 0, end);
                    return trimmed;
                }

                return changed ? bytes : null;
            }

            offset += 12 + (int)length;
            if (isIend)
                break;
        }

        return changed ? bytes : null;
    }

    private static bool HasValidIendCrc(byte[] bytes, int chunkOffset)
    {
        // Empty IEND: length(4) + type(4) + crc(4). CRC covers type+data only.
        return bytes.AsSpan(chunkOffset + 8, 4).SequenceEqual(IendCrc);
    }
}
