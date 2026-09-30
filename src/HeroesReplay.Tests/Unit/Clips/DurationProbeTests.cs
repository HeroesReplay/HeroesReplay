using HeroesReplay.Core.Services.Clips;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
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
    public void OwnedRecordingDurationLog_NamesTheFileSpectateOwns()
    {
        const string owned = @"C:\heroesreplay\Data\Contexts\65581722\match.mp4";
        const string context = @"C:\heroesreplay\Data\Contexts\65581722";
        string chosen = RecordingOwnership.SelectFinalizedFile(owned, context);

        string line = MatchClipExporter.OwnedRecordingDurationLog(owned, context, 181.5);

        Assert.Equal(owned, chosen);
        Assert.Equal("Read duration 181.5s of owned recording " + chosen + ".", line);
        Assert.Null(MatchClipExporter.OwnedRecordingDurationLog(" ", context, 181.5));
        Assert.Null(MatchClipExporter.OwnedRecordingDurationLog(owned, context, null));
    }

    [Fact]
    public void EmptyProbeError_IsRetried()
    {
        Assert.True(MatchClipExporter.RetryDurationProbe(null));
        Assert.True(MatchClipExporter.RetryDurationProbe(""));
    }
}
