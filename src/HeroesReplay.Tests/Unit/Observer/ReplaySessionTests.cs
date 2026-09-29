using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplaySessionTests
{
    [Theory]
    [InlineData(ClientHoldReason.RegionUnavailable, true)]
    [InlineData(ClientHoldReason.VersionMismatch, false)]
    public void Classify_ADialogStaysHeldEvenIfAClockWasSeen(
        ClientHoldReason hold,
        bool matchClockSeen
    )
    {
        Assert.Equal(ReplaySessionKind.Held, ReplaySession.Classify(hold, matchClockSeen));
    }

    [Fact]
    public void Classify_AClockConsumesTheReplay()
    {
        Assert.Equal(
            ReplaySessionKind.Played,
            ReplaySession.Classify(ClientHoldReason.None, matchClockSeen: true)
        );
    }

    [Fact]
    public void Classify_NoClockKeepsTheReplay()
    {
        Assert.Equal(
            ReplaySessionKind.Unplayed,
            ReplaySession.Classify(ClientHoldReason.None, matchClockSeen: false)
        );
    }

    [Fact]
    public void StaysQueued_KeepsHeldAndUnplayed()
    {
        Assert.True(ReplaySession.StaysQueued(ReplaySessionKind.Held));
        Assert.True(ReplaySession.StaysQueued(ReplaySessionKind.Unplayed));
        Assert.False(ReplaySession.StaysQueued(ReplaySessionKind.Played));
    }
}
