using System;
using System.IO;
using HeroesReplay.Core.Obs;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

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
}
