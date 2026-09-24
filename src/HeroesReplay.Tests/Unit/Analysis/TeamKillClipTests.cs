using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Services.Analysis;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis;

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
    }

    [Fact]
    public void Select_SharedKills_EmitsTeamWipeWithoutPentakill()
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

        TeamKillClip clip = Assert.Single(clips);
        Assert.Equal(TeamKillClips.TeamWipeKind, clip.Kind);
        Assert.Equal("Artanis", clip.Hero);
        Assert.Equal(100, clip.FirstDeathSecond);
        Assert.Equal(108, clip.LastDeathSecond);
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
    public void Select_FindsAWipeAfterAnEarlierDeath()
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

        TeamKillClip clip = Assert.Single(clips);
        Assert.Equal(TeamKillClips.TeamWipeKind, clip.Kind);
        Assert.Equal(20, clip.FirstDeathSecond);
        Assert.Equal(28, clip.LastDeathSecond);
        Assert.Equal(8, clip.HudStartSecond);
        Assert.Equal(36, clip.HudEndSecond);
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
