using System.Buffers.Binary;
using System.Text;
using Folio.Domain.Model;

namespace Folio.Ingestion.Media;

/// <summary>Reads intrinsic image dimensions from the first bytes of a file.</summary>
public static class ImageHeader
{
    /// <summary>How many leading bytes measure every format except a JPEG carrying large metadata.</summary>
    public const int ProbeLength = 1024;

    /// <summary>How many leading bytes to read when the first probe was a JPEG that needed more.</summary>
    public const int ExtendedProbeLength = 64 * 1024;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Reads the dimensions of a PNG, JPEG, GIF, WebP or SVG.</summary>
    /// <param name="header">The leading bytes of the file.</param>
    /// <param name="size">The intrinsic size.</param>
    /// <returns><see langword="true" /> if the format was recognized and measured.</returns>
    public static bool TryRead(ReadOnlySpan<byte> header, out MediaSize size)
    {
        size = null!;

        return TryPng(header, ref size)
            || TryGif(header, ref size)
            || TryWebp(header, ref size)
            || TryJpeg(header, ref size)
            || TrySvg(header, ref size);
    }

    /// <summary>Gets whether a longer read could still measure these bytes.</summary>
    /// <remarks>Only JPEG can: metadata segments may push its frame header past the first kilobyte.</remarks>
    /// <param name="header">The bytes already read.</param>
    /// <returns><see langword="true" /> if the bytes are a JPEG whose frame header was not reached.</returns>
    public static bool NeedsLongerRead(ReadOnlySpan<byte> header) =>
        header.Length >= 2 && header[0] == 0xFF && header[1] == 0xD8 && !TryRead(header, out _);

    private static bool TryPng(ReadOnlySpan<byte> header, ref MediaSize size)
    {
        if (header.Length < 24 || !header[..8].SequenceEqual(PngSignature))
        {
            return false;
        }

        size = new MediaSize(
            (int)BinaryPrimitives.ReadUInt32BigEndian(header[16..20]),
            (int)BinaryPrimitives.ReadUInt32BigEndian(header[20..24]));

        return true;
    }

    private static bool TryGif(ReadOnlySpan<byte> header, ref MediaSize size)
    {
        if (header.Length < 10 || !header[..3].SequenceEqual("GIF"u8))
        {
            return false;
        }

        size = new MediaSize(
            BinaryPrimitives.ReadUInt16LittleEndian(header[6..8]),
            BinaryPrimitives.ReadUInt16LittleEndian(header[8..10]));

        return true;
    }

    private static bool TryWebp(ReadOnlySpan<byte> header, ref MediaSize size)
    {
        if (header.Length < 30 || !header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WEBP"u8))
        {
            return false;
        }

        ReadOnlySpan<byte> chunk = header[12..16];

        if (chunk.SequenceEqual("VP8 "u8))
        {
            // A 3-byte frame tag, then the 0x9D012A sync code, then two 14-bit dimensions.
            size = new MediaSize(
                BinaryPrimitives.ReadUInt16LittleEndian(header[26..28]) & 0x3FFF,
                BinaryPrimitives.ReadUInt16LittleEndian(header[28..30]) & 0x3FFF);
            return true;
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            uint packed = BinaryPrimitives.ReadUInt32LittleEndian(header[21..25]);
            size = new MediaSize((int)(packed & 0x3FFF) + 1, (int)((packed >> 14) & 0x3FFF) + 1);
            return true;
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            size = new MediaSize(ReadUInt24(header[24..27]) + 1, ReadUInt24(header[27..30]) + 1);
            return true;
        }

        return false;
    }

    private static bool TryJpeg(ReadOnlySpan<byte> header, ref MediaSize size)
    {
        if (header.Length < 4 || header[0] != 0xFF || header[1] != 0xD8)
        {
            return false;
        }

        int index = 2;

        while (index + 9 < header.Length)
        {
            if (header[index] != 0xFF)
            {
                index++;
                continue;
            }

            byte marker = header[index + 1];

            // Every start-of-frame marker carries the dimensions; DHT, JPG and DAC do not.
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                size = new MediaSize(
                    BinaryPrimitives.ReadUInt16BigEndian(header.Slice(index + 7, 2)),
                    BinaryPrimitives.ReadUInt16BigEndian(header.Slice(index + 5, 2)));
                return true;
            }

            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                index += 2;
                continue;
            }

            index += 2 + BinaryPrimitives.ReadUInt16BigEndian(header.Slice(index + 2, 2));
        }

        return false;
    }

    private static bool TrySvg(ReadOnlySpan<byte> header, ref MediaSize size)
    {
        // Reached only after every binary format declined, so refuse anything holding a NUL.
        if (header.Contains<byte>(0))
        {
            return false;
        }

        string text = Encoding.UTF8.GetString(header);

        if (!text.Contains("<svg", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int? width = Attribute(text, "width");
        int? height = Attribute(text, "height");

        if (width is not null && height is not null)
        {
            size = new MediaSize(width.Value, height.Value);
            return true;
        }

        int box = text.IndexOf("viewBox", StringComparison.OrdinalIgnoreCase);

        if (box < 0)
        {
            return false;
        }

        string[] parts = Value(text, box)
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 4
            || !double.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out double w)
            || !double.TryParse(parts[3], System.Globalization.CultureInfo.InvariantCulture, out double h))
        {
            return false;
        }

        size = new MediaSize((int)Math.Round(w), (int)Math.Round(h));
        return true;
    }

    private static int? Attribute(string text, string name)
    {
        int index = -1;

        for (int at = 0; at < text.Length; at++)
        {
            int found = text.IndexOf(name + "=", at, StringComparison.OrdinalIgnoreCase);

            if (found < 0)
            {
                break;
            }

            // Only an attribute start counts; "stroke-width=" ends with the name we are looking for.
            if (found == 0 || char.IsWhiteSpace(text[found - 1]))
            {
                index = found;
                break;
            }

            at = found;
        }

        if (index < 0)
        {
            return null;
        }

        string value = Value(text, index).TrimEnd('p', 'x', 'P', 'X').Trim();

        return double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? (int)Math.Round(parsed)
            : null;
    }

    private static string Value(string text, int from)
    {
        int quote = text.IndexOf('"', from);
        int end = quote < 0 ? -1 : text.IndexOf('"', quote + 1);

        return quote < 0 || end < 0 ? string.Empty : text[(quote + 1)..end];
    }

    private static int ReadUInt24(ReadOnlySpan<byte> bytes) => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
}
