using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using HeroesReplay.Core.Obs.Inspection;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Inspection;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsScreenshotTests
{
    [Fact]
    public void Capture_ProgramScene_KeepsTheWholeImage()
    {
        string data = Path.Combine(Path.GetTempPath(), "hr-obs-shot-" + Path.GetRandomFileName());
        Directory.CreateDirectory(data);
        try
        {
            FakeObs obs = FakeObs.Installed(data);
            obs.ProgramScene = "waiting-screen";
            obs.Png = TinyPng.Create(160, 90);

            ObsScreenshot shot = ObsScreenshot.Capture(obs.Open(null, null), null, null);

            Assert.Equal("waiting-screen", shot.Source);
            Assert.True(shot.ProgramScene);
            Assert.Equal(160, shot.Width);
            Assert.Equal(90, shot.Height);
            Assert.Equal(obs.Png, shot.Png);
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void Decode_AcceptsADataUriOrBareBase64()
    {
        byte[] png = TinyPng.Create(4, 4);
        string base64 = Convert.ToBase64String(png);

        Assert.Equal(png, ObsScreenshot.Decode("data:image/png;base64," + base64));
        Assert.Equal(png, ObsScreenshot.Decode(base64));
        Assert.Throws<InvalidOperationException>(() => ObsScreenshot.Decode(""));
    }

    [Fact]
    public void Size_IsZeroForBytesThatAreNotAPng()
    {
        Assert.Equal((0, 0), ObsScreenshot.Size(new byte[] { 1, 2, 3 }));
        Assert.Equal((0, 0), ObsScreenshot.Size(null));
    }

    /// <summary>
    /// The fake screenshots are written without System.Drawing (#331), so check that they are
    /// still PNGs: IHDR, IDAT, IEND with valid CRCs, and image data that inflates to one filter
    /// byte plus three bytes per pixel on every row.
    /// </summary>
    [Fact]
    public void TinyPng_IsAValidRgbPng()
    {
        byte[] png = TinyPng.Create(160, 90);

        Assert.Equal((160, 90), ObsScreenshot.Size(png));
        Assert.Equal(TinyPng.Create(160, 90), png);
        var types = new List<string>();
        byte[] idat = null;
        int offset = 8;
        while (offset < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            ReadOnlySpan<byte> typeAndData = png.AsSpan(offset + 4, 4 + length);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length, 4));
            string type = Encoding.ASCII.GetString(typeAndData[..4]);
            Assert.Equal(TinyPng.Crc(typeAndData), crc);
            types.Add(type);
            if (type == "IDAT")
            {
                idat = typeAndData[4..].ToArray();
            }

            offset += 12 + length;
        }

        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, types);
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(idat), CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        Assert.Equal(90 * (1 + 160 * 3), inflated.Length);
    }
}
