using HeroesReplay.Core.Spectating.Capture;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Capture;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PrintWindowCaptureTests
{
    /// <summary>#207: one or two black frames while the client loads are Debug; a long run warns once.</summary>
    [Theory]
    [InlineData(1, LogLevel.Debug)]
    [InlineData(4, LogLevel.Debug)]
    [InlineData(PrintWindowCapture.BlackStreakWarning, LogLevel.Warning)]
    [InlineData(PrintWindowCapture.BlackStreakWarning + 1, LogLevel.Debug)]
    public void BlackCaptureLevel_WarnsOncePerLongBlackRun(int streak, LogLevel expected)
    {
        Assert.Equal(expected, PrintWindowCapture.BlackCaptureLevel(streak));
    }
}
