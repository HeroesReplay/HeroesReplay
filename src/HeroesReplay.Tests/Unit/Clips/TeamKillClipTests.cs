using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Clips;
using Xunit;

namespace HeroesReplay.Tests.Unit.Clips;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class TeamKillClipTests
{
    [Fact]
    public void Select_OneKillerFiveUniqueHeroes_EmitsPentakillAndTeamWipe()
    {
        IReadOnlyList<TeamKillClip> clips = TeamKillClips.Select(
            new[]
            {
                Death(100, "Li-Ming", "Artanis"),
                Death(103, "Li-Ming", "Butcher"),
                Death(106, "Li-Ming", "Chromie"),
                Death(109, "Li-Ming", "Diablo"),
                Death(112, "Li-Ming", "E.T.C."),
            }
        );

        Assert.Equal(2, clips.Count);
        TeamKillClip pentakill = clips.Single(clip => clip.Kind == TeamKillClips.PentakillKind);
        Assert.Equal("Li-Ming", pentakill.Hero);
        Assert.Equal(100, pentakill.FirstDeathSecond);
        Assert.Equal(112, pentakill.LastDeathSecond);
        Assert.Equal(88, pentakill.HudStartSecond);
        Assert.Equal(120, pentakill.HudEndSecond);
        Assert.Equal("Li-Ming pentakill (team wipe)", pentakill.Description);

        TeamKillClip wipe = clips.Single(clip => clip.Kind == TeamKillClips.TeamWipeKind);
        Assert.Equal("Li-Ming", wipe.Hero);
        Assert.Equal(88, wipe.HudStartSecond);
        Assert.Equal(120, wipe.HudEndSecond);
        Assert.Equal("team wipe", wipe.Description);
        Assert.Equal(TeamKillClips.PentakillKind, clips[0].Kind);
        Assert.Equal(5, pentakill.Kills.Count);
        Assert.Equal("Artanis", pentakill.Kills[0].Victim);
        Assert.Equal(100, pentakill.Kills[0].Second);
        Assert.Equal("E.T.C.", pentakill.Kills[4].Victim);
        Assert.Equal(pentakill.Kills[4], wipe.Kills[4]);
    }

    [Fact]
    public void Select_SharedKills_AreNotAClip()
    {
        IReadOnlyList<TeamKillClip> clips = TeamKillClips.Select(
            new[]
            {
                Death(100, "Artanis", "Li-Ming"),
                Death(102, "Artanis", "Butcher"),
                Death(104, "Artanis", "Chromie"),
                Death(106, "Butcher", "Diablo"),
                Death(108, "Chromie", "E.T.C."),
            }
        );

        Assert.Empty(clips);
    }

    [Fact]
    public void Select_SameHeroLabelOnTwoPlayers_AreNotAClip()
    {
        IReadOnlyList<TeamKillClip> clips = TeamKillClips.Select(
            new[]
            {
                new TeamKillDeath(100, "Artanis", "Li-Ming", 0),
                new TeamKillDeath(102, "Artanis", "Butcher", 0),
                new TeamKillDeath(104, "Artanis", "Chromie", 0),
                new TeamKillDeath(106, "Artanis", "Diablo", 1),
                new TeamKillDeath(108, "Artanis", "E.T.C.", 1),
            }
        );

        Assert.Empty(clips);
    }

    [Fact]
    public void Select_RepeatVictim_IsPentakillButNotATeamWipe()
    {
        IReadOnlyList<TeamKillClip> clips = TeamKillClips.Select(
            new[]
            {
                Death(100, "Zeratul", "Artanis"),
                Death(102, "Zeratul", "Butcher"),
                Death(104, "Zeratul", "Chromie"),
                Death(106, "Zeratul", "Diablo"),
                Death(108, "Zeratul", "Artanis"),
            }
        );

        TeamKillClip clip = Assert.Single(clips);
        Assert.Equal(TeamKillClips.PentakillKind, clip.Kind);
        Assert.Equal("Zeratul pentakill", clip.Description);
    }

    [Fact]
    public void Select_DeathsOutsideTheWindow_AreNotATeamWipe()
    {
        IReadOnlyList<TeamKillClip> clips = TeamKillClips.Select(
            new[]
            {
                Death(0, "Artanis", "Li-Ming"),
                Death(3, "Artanis", "Butcher"),
                Death(6, "Artanis", "Chromie"),
                Death(9, "Artanis", "Diablo"),
                Death(22, "Artanis", "E.T.C."),
            }
        );

        Assert.Empty(clips);
    }

    [Fact]
    public void Select_MixedKillsAfterAnEarlierDeath_AreNotAClip()
    {
        IReadOnlyList<TeamKillClip> clips = TeamKillClips.Select(
            new[]
            {
                Death(0, "Artanis", "Li-Ming"),
                Death(20, "Artanis", "Butcher"),
                Death(22, "Butcher", "Chromie"),
                Death(24, "Chromie", "Diablo"),
                Death(26, "Diablo", "E.T.C."),
                Death(28, "Artanis", "Falstad"),
            }
        );

        Assert.Empty(clips);
    }

    [Fact]
    public void Select_ClampsTheLeadAtZero()
    {
        IReadOnlyList<TeamKillClip> clips = TeamKillClips.Select(
            new[]
            {
                Death(2, "Li-Ming", "Artanis"),
                Death(4, "Li-Ming", "Butcher"),
                Death(6, "Li-Ming", "Chromie"),
                Death(8, "Li-Ming", "Diablo"),
                Death(10, "Li-Ming", "E.T.C."),
            }
        );

        Assert.Contains(
            clips,
            clip => clip.Kind == TeamKillClips.PentakillKind && clip.HudStartSecond == 0
        );
    }

    [Fact]
    public void Select_EmptyIsEmpty()
    {
        Assert.Empty(TeamKillClips.Select(null));
        Assert.Empty(TeamKillClips.Select(System.Array.Empty<TeamKillDeath>()));
    }

    private static TeamKillDeath Death(int second, string killer, string victim) =>
        new(second, killer, victim);
}
