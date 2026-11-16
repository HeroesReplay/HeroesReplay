using HeroesReplay.Core.Analysis.Insights.BSteps;
using HeroesReplay.Core.Clips;
using Xunit;

namespace HeroesReplay.Tests.Unit.Clips;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class BStepClipTests
{
    [Fact]
    public void ABstepOverAKill_IsOneClip_FromTheKillToTheBstep_NamedAfterTheBstepper()
    {
        BStep step = Step(
            790,
            791,
            new BStepDeath(5, "Muradin", 789, BStepDeathKind.KilledByBstepper, false, 0)
        );

        TeamKillClip clip = Assert.Single(BStepClips.Select(Insights(step)));

        Assert.Equal(BStepClips.Kind, clip.Kind);
        Assert.Equal("Sonya", clip.Hero);
        Assert.Equal(0, clip.Player);
        Assert.True(clip.OnScreen);
        Assert.Equal(790, clip.FirstDeathSecond);
        Assert.Equal(791, clip.LastDeathSecond);
        Assert.Equal(789 - BStepClips.LeadSeconds, clip.HudStartSecond);
        Assert.Equal(791 + BStepClips.TailSeconds, clip.HudEndSecond);
        Assert.Equal("Sonya bsteps over Muradin (5 hearth cancels in 1 s)", clip.Description);
        Assert.Equal("bstep", TeamKillClips.Title(clip));
    }

    [Fact]
    public void ABstepThenDeath_RunsToTheDeath()
    {
        BStep step = Step(
            360,
            362,
            new BStepDeath(0, "Sonya", 372, BStepDeathKind.Bstepper, true, 5)
        );

        TeamKillClip clip = Assert.Single(BStepClips.Select(Insights(step)));

        Assert.Equal(372 + BStepClips.TailSeconds, clip.HudEndSecond);
        Assert.Equal("Sonya bsteps, then dies (5 hearth cancels in 2 s)", clip.Description);
    }

    [Fact]
    public void ABstepWithNoDeath_OffCamera_OrOverAnotherClip_IsNotCut()
    {
        BStep quiet = Step(100, 102);
        BStep overKill = Step(
            600,
            601,
            new BStepDeath(5, "Muradin", 599, BStepDeathKind.KilledByBstepper, false, 0)
        );
        var kill = new TeamKillClip(
            "triple-kill",
            "Sonya",
            590,
            599,
            578,
            607,
            "Sonya triple kill"
        );

        Assert.Empty(BStepClips.Select(Insights(quiet)));
        Assert.Empty(BStepClips.Select(Insights(overKill), new[] { kill }));
        Assert.Empty(BStepClips.Select(Insights(overKill), onCamera: _ => false));
    }

    [Fact]
    public void OneClipAReplay_ABstepOnAnEnemyBeforeOneByAFallenAlly_ThenTheMostDeaths()
    {
        BStepDeath Enemy(int second) => new(5, "Muradin", second, BStepDeathKind.Enemy, false);
        BStep[] steps =
        {
            Step(100, 101, Enemy(99)),
            Step(300, 301, Enemy(298), Enemy(299)),
            Step(
                500,
                501,
                new BStepDeath(1, "Anduin", 497, BStepDeathKind.Ally, false),
                new BStepDeath(2, "Rexxar", 498, BStepDeathKind.Ally, false),
                new BStepDeath(3, "Tyrael", 499, BStepDeathKind.Ally, false)
            ),
        };

        TeamKillClip clip = Assert.Single(BStepClips.Select(Insights(steps)));

        Assert.Equal(300, clip.FirstDeathSecond);
        Assert.Equal(
            "Sonya bsteps after an ally falls (5 hearth cancels in 1 s)",
            BStepClips.Description(steps[2])
        );
    }

    private static BStep Step(int start, int end, params BStepDeath[] deaths) =>
        new()
        {
            PlayerIndex = 0,
            Team = 0,
            Hero = "Sonya",
            StartSecond = start,
            EndSecond = end,
            Cycles = 5,
            MedianGapSeconds = 0.25,
            Deaths = deaths,
        };

    private static BStepInsights Insights(params BStep[] steps) => new() { BSteps = steps };
}
