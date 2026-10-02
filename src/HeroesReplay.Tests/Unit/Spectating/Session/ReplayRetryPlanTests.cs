using HeroesReplay.Core.Spectating.Session;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Session;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayRetryPlanTests
{
    [Fact]
    public void Decide_ConsumesAVerifiedMatch()
    {
        Assert.Equal(
            ReplayRetryAction.Consume,
            ReplayRetryPlan.Decide(MatchOutcome.VerifiedCompleted, attempt: 1)
        );
        Assert.Equal(
            ReplayRetryAction.Consume,
            ReplayRetryPlan.Decide(MatchOutcome.VerifiedCompleted, attempt: 9)
        );
    }

    [Theory]
    [InlineData(MatchOutcome.LoadTimedOut)]
    [InlineData(MatchOutcome.ClientCrashed)]
    [InlineData(MatchOutcome.VersionMismatch)]
    public void Decide_LeavesTheFrontOnTheFirstMiss(MatchOutcome outcome)
    {
        Assert.Equal(1, ReplayRetryPlan.MaxFrontAttempts);
        Assert.Equal(ReplayRetryAction.Defer, ReplayRetryPlan.Decide(outcome, attempt: 1));
        Assert.Equal(
            ReplayRetryAction.Front,
            ReplayRetryPlan.Decide(outcome, attempt: ReplayRetryPlan.MaxFrontAttempts - 1)
        );
    }

    [Theory]
    [InlineData(MatchOutcome.BuildNotInstalled, false)]
    [InlineData(MatchOutcome.LoadTimedOut, false)]
    [InlineData(MatchOutcome.None, false)]
    [InlineData(MatchOutcome.VerifiedCompleted, false)]
    [InlineData(MatchOutcome.VersionMismatch, true)]
    [InlineData(MatchOutcome.RegionUnavailable, false)]
    [InlineData(MatchOutcome.ClientHung, true)]
    [InlineData(MatchOutcome.ClientCrashed, true)]
    public void ClosesClientAfterDefer_LeavesADownloadAndAMissingBuildAlone(
        MatchOutcome outcome,
        bool closes
    )
    {
        Assert.Equal(closes, ReplayRetryPlan.ClosesClientAfterDefer(outcome));
    }

    [Fact]
    public void Decide_DefersAMissingBuildImmediately()
    {
        Assert.Equal(
            ReplayRetryAction.Defer,
            ReplayRetryPlan.Decide(MatchOutcome.BuildNotInstalled, attempt: 1)
        );
    }

    [Theory]
    [InlineData(MatchOutcome.LoadTimedOut)]
    [InlineData(MatchOutcome.ClientHung)]
    public void Decide_DefersAfterTheFrontBudgetSoTheNextReplayCanRun(MatchOutcome outcome)
    {
        Assert.Equal(
            ReplayRetryAction.Defer,
            ReplayRetryPlan.Decide(outcome, ReplayRetryPlan.MaxFrontAttempts)
        );
    }

    [Fact]
    public void Decide_KeepsARegionDialogOnTheSameReplay()
    {
        Assert.Equal(
            ReplayRetryAction.Front,
            ReplayRetryPlan.Decide(MatchOutcome.RegionUnavailable, attempt: 1)
        );
        Assert.Equal(
            ReplayRetryAction.Front,
            ReplayRetryPlan.Decide(MatchOutcome.RegionUnavailable, attempt: 4)
        );
        Assert.False(ReplayRetryPlan.ClosesClientAfterDefer(MatchOutcome.RegionUnavailable));
    }
}
