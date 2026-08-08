using System.Buffers.Binary;

namespace Folio.Ingestion.Tests;

/// <summary>Builds the smallest byte sequence each format needs to declare its dimensions.</summary>
internal static class Images
{
    /// <summary>Builds a PNG header.</summary>
    /// <param name="width">The width to declare.</param>
    /// <param name="height">The height to declare.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Png(int width, int height)
    {
        byte[] bytes = new byte[24];
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        return bytes;
    }

    /// <summary>Builds a GIF header.</summary>
    /// <param name="width">The width to declare.</param>
    /// <param name="height">The height to declare.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Gif(int width, int height)
    {
        byte[] bytes = new byte[10];
        "GIF89a"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)height);
        return bytes;
    }

    /// <summary>Builds a JPEG whose frame header follows the signature immediately.</summary>
    /// <param name="width">The width to declare.</param>
    /// <param name="height">The height to declare.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Jpeg(int width, int height)
    {
        byte[] bytes = new byte[20];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        bytes[3] = 0xC0;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 17);
        bytes[6] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(7), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(9), (ushort)width);
        return bytes;
    }

    /// <summary>Builds a JPEG whose frame header sits behind an oversized metadata segment.</summary>
    /// <param name="width">The width to declare.</param>
    /// <param name="height">The height to declare.</param>
    /// <param name="metadataBytes">How many bytes of metadata to put in front of the frame.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Padded(int width, int height, int metadataBytes)
    {
        byte[] app1 = new byte[4 + metadataBytes];
        app1[0] = 0xFF;
        app1[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(app1.AsSpan(2), (ushort)(metadataBytes + 2));

        return [0xFF, 0xD8, .. app1, .. Jpeg(width, height)[2..]];
    }

    /// <summary>Builds a lossy WebP header.</summary>
    /// <param name="width">The width to declare.</param>
    /// <param name="height">The height to declare.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Webp(int width, int height)
    {
        byte[] bytes = new byte[30];
        "RIFF"u8.CopyTo(bytes);
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        "VP8 "u8.CopyTo(bytes.AsSpan(12));
        bytes[23] = 0x9D;
        bytes[24] = 0x01;
        bytes[25] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), (ushort)height);
        return bytes;
    }
}
