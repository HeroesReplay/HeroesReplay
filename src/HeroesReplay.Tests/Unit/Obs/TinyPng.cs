using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HeroesReplay.Tests.Unit.Obs;

/// <summary>
/// A noisy RGB PNG written byte by byte, so its base64 is far longer than 100 characters. It
/// used System.Drawing before #331: when several test classes built a <see cref="FakeObs"/> at
/// once, GDI+'s encoder lookup sometimes came back empty and <c>Image.Save</c> threw
/// "Value cannot be null. (Parameter 'encoder')".
/// </summary>
internal static class TinyPng
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Create(int width, int height)
    {
        var random = new Random(7);
        int stride = 1 + width * 3;
        byte[] scanlines = new byte[height * stride];
        for (int y = 0; y < height; y++)
        {
            // Byte 0 of each row is the filter type, 0 (none); then red, green, blue per pixel.
            random.NextBytes(scanlines.AsSpan(y * stride + 1, width * 3));
        }

        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8; // bits per sample
        header[9] = 2; // colour type: RGB
        // Compression, filter and interlace methods stay 0.

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(scanlines);
        }

        using var png = new MemoryStream();
        png.Write(Signature);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>The PNG chunk CRC: CRC-32 of the chunk type and its data.</summary>
    public static uint Crc(ReadOnlySpan<byte> typeAndData)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in typeAndData)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        byte[] typeAndData = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typeAndData.AsSpan(0, 4));
        data.CopyTo(typeAndData, 4);

        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        png.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(typeAndData));
        png.Write(number);
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
