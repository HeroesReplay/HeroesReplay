using System.IO;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Twitch.Rewards;
using Xunit;

namespace HeroesReplay.Tests.Unit.Requests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class WorkEnvelopeTests
{
    [Fact]
    public void TryTransition_AllowsThePlayPathAndRejectsASkipToPlayed()
    {
        Assert.True(WorkEnvelope.TryTransition(WorkState.Accepted, WorkState.Downloading));
        Assert.True(WorkEnvelope.TryTransition(WorkState.Downloading, WorkState.Ready));
        Assert.True(WorkEnvelope.TryTransition(WorkState.Ready, WorkState.Leased));
        Assert.True(WorkEnvelope.TryTransition(WorkState.Leased, WorkState.Launched));
        Assert.True(WorkEnvelope.TryTransition(WorkState.Launched, WorkState.ClockSeen));
        Assert.True(WorkEnvelope.TryTransition(WorkState.ClockSeen, WorkState.VerifiedCompleted));
        Assert.True(WorkEnvelope.TryTransition(WorkState.VerifiedCompleted, WorkState.Fulfilled));
        Assert.False(WorkEnvelope.TryTransition(WorkState.Accepted, WorkState.VerifiedCompleted));
        Assert.False(WorkEnvelope.TryTransition(WorkState.Launched, WorkState.Fulfilled));
        Assert.Equal(WorkState.Quarantined, WorkEnvelope.AfterUnreadable());
        Assert.False(WorkEnvelope.CountsAsPlayed(WorkEnvelope.AfterUnreadable()));
        Assert.True(WorkEnvelope.CountsAsPlayed(WorkState.VerifiedCompleted));
    }

    [Fact]
    public void Replace_LeavesACorruptCopyAndWritesTheNewFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-durable-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "requests.json");
            File.WriteAllText(path, "{");
            DurableFile.Aside(path);

            string[] left = Directory.GetFiles(root);
            Assert.Single(left);
            Assert.Contains("corrupt-", Path.GetFileName(left[0]), System.StringComparison.Ordinal);
            Assert.Equal("{", File.ReadAllText(left[0]));

            DurableFile.Replace(path, "[]");
            Assert.Equal("[]", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Decide_LeavesAnUnverifiedRedemptionUnfulfilledAndFulfillsAVerifiedOne()
    {
        // #169: an unplayed request stays queued, so its redemption is neither fulfilled nor
        // refunded. It plays again.
        Assert.Equal(RedemptionEnd.None, RedemptionDisposition.Decide(false, true));
        Assert.Equal(RedemptionEnd.None, RedemptionDisposition.Decide(true, false));
        Assert.Equal(RedemptionEnd.Fulfill, RedemptionDisposition.Decide(true, true));
    }
}
