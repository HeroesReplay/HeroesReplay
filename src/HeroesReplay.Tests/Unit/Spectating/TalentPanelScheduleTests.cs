using System;
using HeroesReplay.Core.Spectating;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class TalentPanelScheduleTests
{
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan Cluster = TimeSpan.FromSeconds(15);

    [Fact]
    public void ShouldShow_KeepsThePanelUpWhenTheOtherTeamLevelsSoon()
    {
        var times = new[]
        {
            TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(4).Add(TimeSpan.FromSeconds(12)),
        };

        Assert.True(
            TalentPanelSchedule.ShouldShow(
                times,
                TimeSpan.FromMinutes(4).Add(TimeSpan.FromSeconds(10)),
                Hold,
                Cluster
            )
        );
        Assert.True(
            TalentPanelSchedule.ShouldShow(
                times,
                TimeSpan.FromMinutes(4).Add(TimeSpan.FromSeconds(18)),
                Hold,
                Cluster
            )
        );
    }

    [Fact]
    public void ShouldShow_OpensAgainWhenTheNextTalentIsLater()
    {
        var times = new[] { TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(8) };

        Assert.False(TalentPanelSchedule.ShouldShow(times, TimeSpan.FromMinutes(5), Hold, Cluster));
        Assert.True(
            TalentPanelSchedule.ShouldShow(
                times,
                TimeSpan.FromMinutes(8).Add(TimeSpan.FromSeconds(3)),
                Hold,
                Cluster
            )
        );
    }
}
