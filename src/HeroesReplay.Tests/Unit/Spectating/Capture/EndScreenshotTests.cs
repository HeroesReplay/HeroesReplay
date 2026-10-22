using System;
using System.Drawing;
using System.IO;
using HeroesReplay.Core.Spectating.Capture;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Capture;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class EndScreenshotTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-end-capture-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose() => TestTemp.Delete(root);

    /// <summary>
    /// #491: the capture was written as end-HH-MM-SS.png and copied to end.png byte for byte.
    /// It is one end.png now, and a second capture of the same replay replaces it.
    /// </summary>
    [Fact]
    public void Save_WritesOneEndPng_AndASecondCaptureReplacesIt()
    {
        string context = Path.Combine(root, "65581722");
        using (var first = new Bitmap(64, 36))
        {
            Assert.Equal(
                Path.Combine(context, EndScreenshot.FileName),
                EndScreenshot.Save(first, context)
            );
        }

        using (var second = new Bitmap(32, 18))
        {
            EndScreenshot.Save(second, context);
        }

        string only = Assert.Single(Directory.GetFiles(context));
        Assert.Equal("end.png", Path.GetFileName(only));
        using var saved = new Bitmap(only);
        Assert.Equal(new Size(32, 18), saved.Size);
    }
}
