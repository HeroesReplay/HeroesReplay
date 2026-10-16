using System;
using System.Collections.Generic;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Ledger;

namespace HeroesReplay.Core.HeroFocus.Scoring;

/// <summary>Why a candidate cannot be the focus. The first one that applies is reported.</summary>
public static class FocusGates
{
    public const string NotHuman = "not_human";
    public const string NoHero = "no_hero";
    public const string NotStormLeague = "not_storm_league";
    public const string ReplayUsed = "replay_used";
    public const string HeroRepeat = "hero_repeat";
    public const string PlayerCooldown = "player_cooldown";
    public const string Duplicate = "duplicate";
    public const string StrengthUnknown = "strength_unknown";
    public const string StrengthGames = "strength_games";
    public const string StrengthWinRate = "strength_win_rate";
    public const string QualityFloor = "quality_floor";
}

/// <summary>
/// One player of one waiting replay, scored as a focus (#433). <see cref="Gate"/> is null when
/// the player may be picked. <see cref="Parts"/> holds every part of the score by name.
/// </summary>
public sealed record FocusCandidate
{
    public string Key { get; init; }
    public int? ReplayId { get; init; }
    public int PlayerIndex { get; init; }

    /// <summary><c>Name#1234</c>; local only.</summary>
    public string Player { get; init; }

    public int RegionId { get; init; }
    public string Hero { get; init; }
    public string AttributeId { get; init; }
    public string Role { get; init; }
    public string Build { get; init; }
    public double? BuildScore { get; init; }

    /// <summary>The build is a clear guide match (<see cref="FocusScorer.BuildClarity"/> 1), so it may be named.</summary>
    public bool BuildClear { get; init; }

    public string Map { get; init; }
    public string Rank { get; init; }
    public string League { get; init; }
    public string Region { get; init; }
    public string GameVersion { get; init; }
    public string AllyComp { get; init; }
    public string EnemyComp { get; init; }
    public FocusStrength Strength { get; init; }

    public string Gate { get; init; }
    public double Quality { get; init; }
    public double Novelty { get; init; }
    public double Score { get; init; }

    public IReadOnlyDictionary<string, double> Parts { get; init; } =
        new Dictionary<string, double>();

    public bool Eligible => Gate == null;

    public FocusLedgerEntry ToEntry(FocusEntryState state, DateTime nowUtc) =>
        new()
        {
            Key = Key,
            ReplayId = ReplayId,
            State = state,
            PickedAtUtc = nowUtc,
            PlayerIndex = PlayerIndex,
            Player = Player,
            RegionId = RegionId,
            Hero = Hero,
            AttributeId = AttributeId,
            Role = Role,
            Build = Build,
            BuildScore = BuildScore,
            BuildClear = BuildClear,
            Map = Map,
            League = League,
            Rank = Rank,
            Region = Region,
            GameVersion = GameVersion,
            AllyComp = AllyComp,
            EnemyComp = EnemyComp,
            // Only whether the record was known: the ledger outlives the player records the
            // privacy feed purges, so it keeps none of Heroes Profile's numbers.
            Strength = Strength == null ? null : new FocusStrength { Status = Strength.Status },
            Quality = Quality,
            Novelty = Novelty,
            Score = Score,
            Parts = new Dictionary<string, double>(Parts),
        };
}
