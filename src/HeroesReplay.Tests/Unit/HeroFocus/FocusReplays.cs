using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.Shared;
using ReplayUnit = Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.HeroFocus;

/// <summary>Parsed replays built by hand: ten human players on one catalog hero each.</summary>
internal static class FocusReplays
{
    /// <summary>Ten heroes with roles, unit ids <c>HeroH0</c>…<c>HeroH9</c>, attribute ids <c>H0</c>…<c>H9</c>.</summary>
    public static readonly IReadOnlyList<Hero> Catalog = Heroes();

    public static readonly GuideCatalog NoGuides = new(
        new GuideReference { CheckedAt = new DateOnly(2026, 10, 12) }
    );

    /// <summary>
    /// A Storm League replay on Alterac Pass: player i plays hero Hi, is named Pi#100i in EU, and
    /// has <c>kills[i]</c> solo kills (one each by default). Team 0 wins.
    /// </summary>
    public static Replay Replay(params int[] kills)
    {
        var players = new Player[10];
        for (int index = 0; index < 10; index++)
        {
            players[index] = new Player
            {
                Name = "P" + index,
                BattleTag = 1000 + index,
                BattleNetRegionId = 2,
                PlayerType = PlayerType.Human,
                Team = index < 5 ? 0 : 1,
                IsWinner = index < 5,
                ScoreResult = new ScoreResult
                {
                    SoloKills = index < kills.Length ? kills[index] : 1,
                    Deaths = 2,
                    Assists = 3,
                    Takedowns = 4,
                    HeroDamage = 20000,
                    MatchAwards = new List<MatchAwardType>(),
                },
                HeroUnits = new List<ReplayUnit>
                {
                    new() { Name = "HeroH" + index, TimeSpanBorn = TimeSpan.Zero },
                },
            };
        }

        return new Replay
        {
            Map = "Alterac Pass",
            GameMode = GameMode.StormLeague,
            ReplayVersion = "2.57.0.98348",
            Timestamp = new DateTime(2026, 10, 15, 20, 0, 0, DateTimeKind.Utc),
            Frames = 1200 * 16,
            Players = players,
        };
    }

    private static IReadOnlyList<Hero> Heroes()
    {
        string[] roles =
        {
            "Tank",
            "Bruiser",
            "Healer",
            "Ranged Assassin",
            "Melee Assassin",
            "Support",
            "Tank",
            "Healer",
            "Ranged Assassin",
            "Bruiser",
        };
        var heroes = new List<Hero>();
        for (int index = 0; index < roles.Length; index++)
        {
            heroes.Add(
                new Hero("Hero " + index, "HeroH" + index, null, "H" + index, role: roles[index])
            );
        }

        return heroes;
    }
}
