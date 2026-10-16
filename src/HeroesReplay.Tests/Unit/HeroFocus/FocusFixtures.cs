using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Ledger;

namespace HeroesReplay.Tests.Unit.HeroFocus;

/// <summary>Focus cards and ledger entries built by hand, for the scorer and the ledger.</summary>
internal static class FocusFixtures
{
    public static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A Storm League card with ten players: <paramref name="star"/> at index 0 on team 0 (the
    /// winners) and nine ordinary players around it.
    /// </summary>
    public static FocusCard Card(
        int replayId,
        FocusCardPlayer star,
        string map = "Alterac Pass",
        string rank = "Diamond 3",
        string region = "EU",
        string mode = "StormLeague",
        double lengthSeconds = 1200
    )
    {
        var players = new List<FocusCardPlayer> { star };
        for (int index = 1; index < 10; index++)
        {
            players.Add(
                Player(
                    index,
                    "Filler" + index,
                    "Fill" + index,
                    "Ranged Assassin",
                    team: index < 5 ? 0 : 1,
                    kills: 1,
                    deaths: 4,
                    assists: 3,
                    heroDamage: 20000
                )
            );
        }

        foreach (FocusCardPlayer player in players)
        {
            player.Won = player.Team == 0;
        }

        return new FocusCard
        {
            Key = replayId.ToString(),
            ReplayId = replayId,
            Map = map,
            GameMode = mode,
            Rank = rank,
            Region = region,
            GameVersion = "2.57.0.98348",
            LengthSeconds = lengthSeconds,
            WinnerTeam = 0,
            Players = players,
        };
    }

    public static FocusCardPlayer Player(
        int index,
        string hero,
        string attributeId,
        string role,
        int team = 0,
        int kills = 8,
        int deaths = 1,
        int assists = 12,
        int heroDamage = 60000,
        string build = "Main Build",
        double buildScore = 0.9,
        FocusStrength strength = null,
        string name = null,
        int battleTag = 0,
        bool ai = false,
        string unitId = null,
        params string[] awards
    ) =>
        new()
        {
            Index = index,
            Team = team,
            IsAi = ai,
            Name = name ?? "Player" + index,
            BattleTag = battleTag == 0 ? 1000 + index : battleTag,
            RegionId = 2,
            Hero = hero,
            AttributeId = attributeId,
            UnitId = unitId ?? "Hero" + attributeId,
            Role = role,
            Stats = new Dictionary<string, double>
            {
                ["SoloKills"] = kills,
                ["Deaths"] = deaths,
                ["Assists"] = assists,
                ["Takedowns"] = kills + assists,
                ["HeroDamage"] = heroDamage,
                ["TeamfightHeroDamage"] = heroDamage / 2,
                ["Multikill"] = kills / 3,
                ["Healing"] = 0,
                ["DamageTaken"] = 30000,
            },
            Awards = awards.ToList(),
            Build =
                build == null
                    ? null
                    : new FocusCardBuild
                    {
                        Name = build,
                        Score = buildScore,
                        Tiers = 7,
                    },
            Strength = strength,
        };

    public static FocusStrength Strength(int wins, int games) =>
        new()
        {
            Status = FocusStrengthStatus.Known,
            Wins = wins,
            Games = games,
        };

    public static FocusLedgerEntry Entry(
        string attributeId,
        string role,
        DateTime pickedAt,
        string build = "Main Build",
        string map = "Alterac Pass",
        string player = null,
        int? replayId = null,
        FocusEntryState state = FocusEntryState.Played,
        string key = null
    ) =>
        new()
        {
            Key = key ?? Guid.NewGuid().ToString("N"),
            ReplayId = replayId,
            State = state,
            PickedAtUtc = pickedAt,
            Hero = attributeId,
            AttributeId = attributeId,
            Role = role,
            Build = build,
            Map = map,
            Player = player,
            RegionId = 2,
            EnemyComp = "none",
        };
}
