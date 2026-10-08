using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.Core.Clips;
using Xunit;

namespace HeroesReplay.Tests.Unit.Check;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class FfmpegCheckResultTests
{
    [Fact]
    public void ThePinnedBuild_IsOk()
    {
        CheckCommand.CheckResult result = CheckCommand.ToCheckResult(
            new FfmpegCheckReport(true, false, "ffmpeg: path: ffmpeg version 9.0.2")
        );

        Assert.Equal("ffmpeg", result.Name);
        Assert.True(result.Ok);
        Assert.Equal("ffmpeg: path: ffmpeg version 9.0.2", result.Detail);
    }

    [Fact]
    public void AnotherVersion_PassesAsAWarning()
    {
        CheckCommand.CheckResult result = CheckCommand.ToCheckResult(
            new FfmpegCheckReport(true, true, "ffmpeg: Not the pinned 9.0.2")
        );

        Assert.True(result.Ok);
        Assert.StartsWith(CheckCommand.WarningPrefix, result.Detail);
    }

    [Fact]
    public void AMissingTool_Fails()
    {
        CheckCommand.CheckResult result = CheckCommand.ToCheckResult(
            new FfmpegCheckReport(false, false, "ffprobe: not found")
        );

        Assert.False(result.Ok);
        Assert.Equal("ffprobe: not found", result.Detail);
    }
}
