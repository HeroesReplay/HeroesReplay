using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Spectating;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class LauncherRecoveryPlanTests
{
    [Fact]
    public void Decide_RestartsTheLauncherOnceThenRequiresAnOperator()
    {
        Assert.Equal(
            LauncherRecoveryAction.RestartLauncher,
            LauncherRecoveryPlan.Decide(ClientHoldReason.VersionMismatch, 0)
        );
        Assert.Equal(
            LauncherRecoveryAction.OperatorRequired,
            LauncherRecoveryPlan.Decide(ClientHoldReason.VersionMismatch, 1)
        );
        Assert.Equal(
            LauncherRecoveryAction.OperatorRequired,
            LauncherRecoveryPlan.Decide(ClientHoldReason.VersionMismatch, 4)
        );
        Assert.Equal(
            LauncherRecoveryAction.None,
            LauncherRecoveryPlan.Decide(ClientHoldReason.RegionUnavailable, 0)
        );
        Assert.Equal(ReplaySessionKind.Held, ReplaySession.Classify(MatchOutcome.VersionMismatch));
        Assert.True(ReplaySession.StaysQueued(ReplaySessionKind.Held));
    }
}
