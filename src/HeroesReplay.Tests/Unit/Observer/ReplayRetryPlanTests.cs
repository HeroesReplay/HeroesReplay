using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

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
    [InlineData(MatchOutcome.RegionUnavailable)]
    public void Decide_KeepsTheFirstFailureAtTheFront(MatchOutcome outcome)
    {
        Assert.Equal(ReplayRetryAction.Front, ReplayRetryPlan.Decide(outcome, attempt: 1));
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
    [InlineData(MatchOutcome.RegionUnavailable)]
    public void Decide_DefersAfterTheFrontBudgetSoTheNextReplayCanRun(MatchOutcome outcome)
    {
        Assert.Equal(
            ReplayRetryAction.Defer,
            ReplayRetryPlan.Decide(outcome, ReplayRetryPlan.MaxFrontAttempts)
        );
    }
}
