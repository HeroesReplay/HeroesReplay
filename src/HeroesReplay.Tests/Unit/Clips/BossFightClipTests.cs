using System.Collections.Generic;
using HeroesReplay.Core.Analysis.Insights.Bosses;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using HeroesReplay.Core.Clips;
using Xunit;

namespace HeroesReplay.Tests.Unit.Clips;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class BossFightClipTests
{
    [Fact]
    public void Steal_IsAClipNamedAfterTheBossKiller_FromTheFightToJustAfterTheCapture()
    {
        TeamKillClip clip = Assert.Single(
            BossFightClips.Select(Insights(Fight(BossFightKind.Steal, second: 600, start: 580)))
        );

        Assert.Equal(BossFightClips.StealKind, clip.Kind);
        Assert.Equal("boss steal", TeamKillClips.Title(clip));
        Assert.Equal("Li-Ming", clip.Hero);
        Assert.Equal(577, clip.HudStartSecond);
        Assert.Equal(606, clip.HudEndSecond);
        Assert.Equal(
            "Li-Ming steals the boss for Red: Blue had up to 4 heroes on it for 15 s",
            clip.Description
        );
        Assert.False(clip.WipedTeam);
    }

    [Fact]
    public void ABossStolenFromTheTeamThatKilledIt_NamesTheCapturingTeam()
    {
        BossFight fight = Fight(BossFightKind.Steal, second: 600, start: 580) with
        {
            KillerTeam = 0,
            KillerHero = "Valla",
        };

        TeamKillClip clip = Assert.Single(BossFightClips.Select(Insights(fight)));

        Assert.True(clip.NamesTeam);
        Assert.Equal("Red team", clip.Hero);
        Assert.Equal(
            "Red steals the boss Blue killed (Valla landed the killing blow)",
            clip.Description
        );
    }

    [Fact]
    public void AStealThatFailedAtTheCircle_IsADefendedBossNamingTheTeamThatKeptIt()
    {
        BossFight fight = Fight(BossFightKind.Defended, second: 600, start: 580) with
        {
            KillerTeam = 0,
            KillerHero = "Valla",
        };

        TeamKillClip clip = Assert.Single(BossFightClips.Select(Insights(fight)));

        Assert.Equal("boss defended", TeamKillClips.Title(clip));
        Assert.Equal("Red team", clip.Hero);
        Assert.Equal(
            "Red keeps the boss Blue tried to steal (Valla landed the killing blow)",
            clip.Description
        );
    }

    [Fact]
    public void ALongFight_IsCutToItsLastSeconds()
    {
        TeamKillClip clip = Assert.Single(
            BossFightClips.Select(Insights(Fight(BossFightKind.Defended, second: 600, start: 510)))
        );

        Assert.Equal("boss defended", TeamKillClips.Title(clip));
        Assert.Equal(
            600 - BossFightClips.MaxFightSeconds - BossFightClips.LeadSeconds,
            clip.HudStartSecond
        );
        Assert.Equal(
            "Li-Ming secures the boss for Red through a 12 s fight at the pit, 4 heroes down",
            clip.Description
        );
    }

    [Fact]
    public void OrdinaryTakes_AreNotClips()
    {
        Assert.Empty(BossFightClips.Select(Insights(Fight(BossFightKind.Take, 600, 580))));
        Assert.Empty(BossFightClips.Select(null));
    }

    [Fact]
    public void StealsComeFirst_AtMostTwo_EarliestShownFirst()
    {
        IReadOnlyList<TeamKillClip> clips = BossFightClips.Select(
            Insights(
                Fight(BossFightKind.Defended, 300, 280),
                Fight(BossFightKind.Steal, 900, 880),
                Fight(BossFightKind.Steal, 1200, 1180)
            )
        );

        Assert.Equal(BossFightClips.MaxClips, clips.Count);
        Assert.All(clips, clip => Assert.Equal(BossFightClips.StealKind, clip.Kind));
        Assert.Equal(877, clips[0].HudStartSecond);
    }

    [Fact]
    public void AFightAKillClipAlreadyCovers_IsNotCutTwice()
    {
        IReadOnlyList<TeamKillClip> kills = TeamKillClips.Select(
            new[]
            {
                new TeamKillDeath(590, "Valla", "Li-Ming"),
                new TeamKillDeath(591, "Valla", "Johanna"),
                new TeamKillDeath(592, "Valla", "Anduin"),
            }
        );

        Assert.Empty(BossFightClips.Select(Insights(Fight(BossFightKind.Steal, 600, 580)), kills));
    }

    [Fact]
    public void AStealTheCameraDidNotShow_IsSkipped_AndTheNextFightTakesItsPlace()
    {
        IReadOnlyList<TeamKillClip> clips = BossFightClips.Select(
            Insights(Fight(BossFightKind.Steal, 900, 880), Fight(BossFightKind.Defended, 300, 280)),
            onCamera: clip => clip.Kind != BossFightClips.StealKind
        );

        TeamKillClip clip = Assert.Single(clips);
        Assert.Equal(BossFightClips.DefendedKind, clip.Kind);
    }

    [Fact]
    public void ABossClip_IsPlacedAtThePit()
    {
        BossFight fight = Fight(BossFightKind.Steal, 600, 580) with
        {
            Point = new MapPoint(99, 62),
        };

        TeamKillClip clip = Assert.Single(BossFightClips.Select(Insights(fight)));

        Assert.Equal(new MapPoint(99, 62), clip.Point);
    }

    private static BossFightInsights Insights(params BossFight[] fights) =>
        new() { Fights = fights };

    /// <summary>Red (1) took the boss Blue (0) started; Li-Ming landed the blow.</summary>
    private static BossFight Fight(BossFightKind kind, int second, int start) =>
        new()
        {
            Team = 1,
            Second = second,
            StartSecond = start,
            Starter = kind == BossFightKind.Steal ? 0 : 1,
            KillerIndex = 5,
            KillerHero = "Li-Ming",
            KillerTeam = 1,
            TogetherSeconds = 12,
            Kind = kind,
            Teams = new[]
            {
                new BossFightTeam
                {
                    Team = 0,
                    GroupSeconds = 15,
                    MostHeroes = 4,
                    Deaths = 3,
                },
                new BossFightTeam { Team = 1, Deaths = 1 },
            },
        };
}
