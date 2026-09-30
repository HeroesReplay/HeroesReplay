using HeroesReplay.Core.Services.Clips;
using Xunit;

namespace HeroesReplay.Tests.Unit.Clips;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class DurationProbeTests
{
    [Fact]
    public void MoovAtomNotFound_IsRetried()
    {
        Assert.True(
            MatchClipExporter.RetryDurationProbe(
                "[mov,mp4,m4a,3gp,3g2,mj2] moov atom not found\nInvalid data found when processing input"
            )
        );
    }

    [Fact]
    public void MissingFfprobe_IsNotRetried()
    {
        Assert.False(
            MatchClipExporter.RetryDurationProbe(
                "ffprobe was not found. The system cannot find the file specified."
            )
        );
    }

    [Fact]
    public void CannotFindTheFile_IsNotRetried()
    {
        Assert.False(MatchClipExporter.RetryDurationProbe("cannot find the file specified"));
    }

    [Fact]
    public void FfprobeDidNotStart_IsNotRetried()
    {
        Assert.False(MatchClipExporter.RetryDurationProbe("ffprobe did not start."));
    }

    [Fact]
    public void EmptyProbeError_IsRetried()
    {
        Assert.True(MatchClipExporter.RetryDurationProbe(null));
        Assert.True(MatchClipExporter.RetryDurationProbe(""));
    }
}
