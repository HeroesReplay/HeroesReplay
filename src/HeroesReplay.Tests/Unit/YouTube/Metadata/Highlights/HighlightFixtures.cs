using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Builds;
using HeroesReplay.Core.Analysis.Insights.Camps;
using HeroesReplay.Core.Analysis.Insights.Comeback;
using HeroesReplay.Core.Analysis.Insights.Finish;
using HeroesReplay.Core.Analysis.Insights.Focused;
using HeroesReplay.Core.Analysis.Insights.Lanes;
using HeroesReplay.Core.Analysis.Insights.Players;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.YouTube.Metadata.Highlights;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata.Highlights;

/// <summary>
/// A match for highlight tests. Blue: Li-Ming (0), Valla (1), Muradin (2). Red: Orphea (5), Thrall (6).
/// Li-Ming is deathless at 12/0/9 on a strong Teleport Build; Orphea is the MVP at 7/1/5.
/// </summary>
internal static class HighlightFixtures
{
    public static readonly PlayerLine LiMing = Line(0, 0, "Li-Ming", 12, 0, 9);
    public static readonly PlayerLine Valla = Line(1, 0, "Valla", 3, 4, 10);
    public static readonly PlayerLine Muradin = Line(2, 0, "Muradin", 1, 5, 14);
    public static readonly PlayerLine Orphea = Line(5, 1, "Orphea", 7, 1, 5) with { Mvp = true };
    public static readonly PlayerLine Thrall = Line(6, 1, "Thrall", 4, 6, 8);

    public static MatchInsights Insights(
        bool coreRace = false,
        bool comeback = false,
        LaneConfidence blueLane = LaneConfidence.None,
        LaneConfidence redLane = LaneConfidence.None,
        double buildScore = 1.0,
        string buildName = "Teleport Build",
        bool ambiguous = false,
        bool deathless = true,
        int redCamps = 6
    ) =>
        new()
        {
            Players = new PlayerInsights
            {
                Lines = new[] { LiMing, Valla, Muradin, Orphea, Thrall },
                Mvp = Orphea,
                Standouts = deathless
                    ? new[] { new Standout(StandoutKind.DeathlessCarry, LiMing) }
                    : new Standout[0],
            },
            Lanes = new LaneInsights
            {
                LaneCount = 3,
                Teams = new[] { Lane(0, "Muradin", blueLane), Lane(1, "Thrall", redLane) },
            },
            Camps = new CampInsights
            {
                TotalCaptures = 9 + redCamps,
                Teams = new[]
                {
                    new TeamCamps
                    {
                        Team = 0,
                        Captures = 9,
                        EnemySideCaptures = 4,
                    },
                    new TeamCamps
                    {
                        Team = 1,
                        Captures = redCamps,
                        Drought = redCamps <= 1,
                    },
                },
            },
            Finish = new FinishInsights { CoreRace = coreRace },
            Comeback = new ComebackInsights { Comeback = comeback },
            Builds = new BuildInsights
            {
                Players = new[]
                {
                    new PlayerBuild
                    {
                        PlayerIndex = 0,
                        Team = 0,
                        Hero = "Li-Ming",
                        Build = buildName,
                        Tag = "Recommended",
                        Score = buildScore,
                        Recommended = 6,
                        Tiers = 7,
                        Ambiguous = ambiguous,
                        GuideUrl = "https://www.icy-veins.com/heroes/li-ming-build-guide",
                    },
                    new PlayerBuild
                    {
                        PlayerIndex = 6,
                        Team = 1,
                        Hero = "Thrall",
                        Build = "Chain Lightning Build",
                        Score = 0.75,
                        Recommended = 4,
                        Situational = 2,
                        Tiers = 6,
                        GuideUrl = "https://www.icy-veins.com/heroes/thrall-build-guide",
                    },
                    new PlayerBuild
                    {
                        PlayerIndex = 2,
                        Team = 0,
                        Hero = "Muradin",
                        Build = "Dwarf Toss Build",
                        Score = 0.5,
                        Recommended = 3,
                        Tiers = 6,
                        GuideUrl = "https://www.icy-veins.com/heroes/muradin-build-guide",
                    },
                },
            },
            Focus = new FocusInsights { Top = new FocusShare(1, 0, "Valla", 300, 0.3) },
        };

    public static HighlightContext Context(
        MatchInsights insights,
        IReadOnlyList<TeamKillClip> pentakills = null,
        HighlightSettings settings = null
    ) =>
        new(
            insights,
            pentakills ?? new TeamKillClip[0],
            settings ?? new HighlightSettings { Enabled = true }
        );

    public static TeamKillClip Pentakill(string hero, bool wiped = false) =>
        new(
            TeamKillClips.PentakillKind,
            hero,
            100,
            140,
            60,
            148,
            hero + " pentakill",
            Enumerable
                .Range(0, 5)
                .Select(i => new TeamKillBlow(100 + i * 10, wiped ? "Hero" + i : "Hero"))
                .ToArray()
        );

    private static PlayerLine Line(
        int index,
        int team,
        string hero,
        int kills,
        int deaths,
        int assists
    ) =>
        new()
        {
            PlayerIndex = index,
            Team = team,
            Hero = hero,
            Kills = kills,
            Deaths = deaths,
            Assists = assists,
        };

    private static TeamLanes Lane(int team, string hero, LaneConfidence confidence) =>
        new()
        {
            Team = team,
            GroupLane = 2,
            Confidence = confidence,
            SoloLaner =
                confidence == LaneConfidence.None
                    ? null
                    : new LaneSoaker
                    {
                        PlayerIndex = team * 5,
                        Hero = hero,
                        Lane = 1,
                        Mirrored = true,
                    },
        };
}
