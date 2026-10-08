using System.Collections.Generic;
using HeroesReplay.Core.Clips;
using Xunit;

namespace HeroesReplay.Tests.Unit.Clips;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class TeamKillClipTests
{
    [Fact]
    public void Select_OneKillerFiveUniqueHeroes_IsOnePentakillClip_NotASecondTeamWipeClip()
    {
        // #369: replays 65745237 and 65773257 cut the same window twice, once as a team wipe.
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

        TeamKillClip pentakill = Assert.Single(clips);
        Assert.Equal(TeamKillClips.PentakillKind, pentakill.Kind);
        Assert.Equal("Li-Ming", pentakill.Hero);
        Assert.Equal(100, pentakill.FirstDeathSecond);
        Assert.Equal(112, pentakill.LastDeathSecond);
        Assert.Equal(88, pentakill.HudStartSecond);
        Assert.Equal(120, pentakill.HudEndSecond);
        Assert.Equal("Li-Ming pentakill (team wipe)", pentakill.Description);
        Assert.True(pentakill.WipedTeam);
        Assert.Equal(5, pentakill.Kills.Count);
        Assert.Equal("Artanis", pentakill.Kills[0].Victim);
        Assert.Equal(100, pentakill.Kills[0].Second);
        Assert.Equal("E.T.C.", pentakill.Kills[4].Victim);
    }

    [Fact]
    public void Select_ReturnsOnlyIndividualPentakills()
    {
        IReadOnlyList<TeamKillClip> clips = TeamKillClips.Select(
            new[]
            {
                // Li-Ming wipes the team on her own.
                Death(100, "Li-Ming", "Artanis"),
                Death(102, "Li-Ming", "Butcher"),
                Death(104, "Li-Ming", "Chromie"),
                Death(106, "Li-Ming", "Diablo"),
                Death(108, "Li-Ming", "E.T.C."),
                // Later the whole team dies again, but three players share the kills.
                Death(300, "Li-Ming", "Artanis"),
                Death(301, "Li-Ming", "Butcher"),
                Death(302, "Valla", "Chromie"),
                Death(303, "Valla", "Diablo"),
                Death(304, "Jaina", "E.T.C."),
            }
        );

        TeamKillClip clip = Assert.Single(clips);
        Assert.Equal(TeamKillClips.PentakillKind, clip.Kind);
        Assert.Equal(100, clip.FirstDeathSecond);
        Assert.All(clips, item => Assert.Equal(TeamKillClips.PentakillKind, item.Kind));
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
        Assert.False(clip.WipedTeam);
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

        TeamKillClip clip = Assert.Single(clips);
        Assert.Equal(TeamKillClips.PentakillKind, clip.Kind);
        Assert.Equal(0, clip.HudStartSecond);
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
