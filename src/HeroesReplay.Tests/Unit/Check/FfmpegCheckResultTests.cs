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
        Assert.Equal(CheckCodes.FfmpegOk, result.Code);
    }

    [Fact]
    public void AnotherVersion_PassesAsAWarning()
    {
        CheckCommand.CheckResult result = CheckCommand.ToCheckResult(
            new FfmpegCheckReport(true, true, "ffmpeg: Not the pinned 9.0.2", FfmpegCheck.NotPinned)
        );

        Assert.True(result.Ok);
        Assert.StartsWith(CheckCommand.WarningPrefix, result.Detail);
        Assert.Equal("check.ffmpeg.not_pinned", result.Code);
        Assert.Equal(CheckEntry.Warn, CheckEntry.From(result).Status);
    }

    [Theory]
    [InlineData(FfmpegCheck.Missing, "check.ffmpeg.missing")]
    [InlineData(FfmpegCheck.NotRunnable, "check.ffmpeg.not_runnable")]
    [InlineData(FfmpegCheck.NoLibx264, "check.ffmpeg.libx264_missing")]
    public void AnUnusableTool_Fails(string reason, string code)
    {
        CheckCommand.CheckResult result = CheckCommand.ToCheckResult(
            new FfmpegCheckReport(false, false, "ffprobe: not found", reason)
        );

        Assert.False(result.Ok);
        Assert.Equal("ffprobe: not found", result.Detail);
        Assert.Equal(code, result.Code);
        Assert.Contains(code, CheckCodes.All);
    }
}
