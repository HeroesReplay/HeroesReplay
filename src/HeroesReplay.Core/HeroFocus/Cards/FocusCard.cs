using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.HeroFocus.Cards;

/// <summary>
/// One replay's focus candidates (#433), read once from the parsed replay so the spectator can
/// choose a focus replay without parsing every waiting file. Raw facts only: how good a game was
/// is judged when a focus is picked, because the hero baselines keep moving.
/// </summary>
public sealed class FocusCard
{
    /// <summary>Raised when the card gains or changes a fact; an older card is built again.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>The replay id as text, else the replay file's name without its extension.</summary>
    public string Key { get; set; }

    public int? ReplayId { get; set; }

    public string File { get; set; }

    /// <summary>The English catalog map name.</summary>
    public string Map { get; set; }

    /// <summary>The parser's game mode (<c>StormLeague</c>, <c>QuickMatch</c>, …).</summary>
    public string GameMode { get; set; }

    /// <summary>Heroes Profile's rank or league tier (<c>Diamond 3</c>), when known.</summary>
    public string Rank { get; set; }

    public double? AverageMmr { get; set; }

    /// <summary>NA, EU, KR or CN, from the players' Battle.net region.</summary>
    public string Region { get; set; }

    public string GameVersion { get; set; }

    public DateTime? GameDateUtc { get; set; }

    public double LengthSeconds { get; set; }

    public int? WinnerTeam { get; set; }

    /// <summary>The talent-build reference's check date the builds were matched against.</summary>
    public string GuideCheckedAt { get; set; }

    public DateTime BuiltAtUtc { get; set; }

    public List<FocusCardPlayer> Players { get; set; } = new();
}

public sealed class FocusCardPlayer
{
    /// <summary>The player's index in the replay's player list (the spectator's 1-0 key).</summary>
    public int Index { get; set; }

    public int Team { get; set; }

    public bool Won { get; set; }

    public bool IsAi { get; set; }

    /// <summary>The Battle.net name without its number.</summary>
    public string Name { get; set; }

    /// <summary>The BattleTag number (<c>1234</c> in <c>Name#1234</c>).</summary>
    public int BattleTag { get; set; }

    /// <summary>The Battle.net region id: 1 NA, 2 EU, 3 KR, 5 CN.</summary>
    public int RegionId { get; set; }

    /// <summary>The catalog hero's English name.</summary>
    public string Hero { get; set; }

    public string AttributeId { get; set; }

    public string UnitId { get; set; }

    /// <summary>The catalog role: Tank, Bruiser, Melee Assassin, Ranged Assassin, Healer or Support.</summary>
    public string Role { get; set; }

    /// <summary>The score screen by <see cref="ScoreStats"/> name.</summary>
    public Dictionary<string, double> Stats { get; set; } = new();

    public List<string> Awards { get; set; } = new();

    public FocusCardBuild Build { get; set; }

    /// <summary>The team-composition labels of the player's team.</summary>
    public List<string> AllyComp { get; set; } = new();

    /// <summary>The team-composition labels of the other team.</summary>
    public List<string> EnemyComp { get; set; } = new();

    /// <summary>The player's Heroes Profile record on this hero, once it has been looked up.</summary>
    public FocusStrength Strength { get; set; }

    /// <summary><c>Name#1234</c>, the form Heroes Profile and the requests take.</summary>
    [JsonIgnore]
    public string Player =>
        string.IsNullOrWhiteSpace(Name) || BattleTag <= 0 ? null : Name.Trim() + "#" + BattleTag;

    public double Stat(string name) =>
        name != null && Stats != null && Stats.TryGetValue(name, out double value) ? value : 0;
}

/// <summary>The guide build the player's talents fit best (<c>BuildMatcher</c>).</summary>
public sealed class FocusCardBuild
{
    public string Name { get; set; }
    public string Tag { get; set; }

    /// <summary>0 to 1: 1 when every pick is one the build recommends.</summary>
    public double Score { get; set; }

    public bool Ambiguous { get; set; }

    public int Tiers { get; set; }
}

public enum FocusStrengthStatus
{
    /// <summary>Heroes Profile answered with the player's games on the hero.</summary>
    Known,

    /// <summary>Not looked up, or the lookup failed or was out of budget.</summary>
    Unknown,

    /// <summary>The player's profile is private.</summary>
    Private,
}

/// <summary>The player's Storm League record on the hero, from Heroes Profile.</summary>
public sealed class FocusStrength
{
    [JsonConverter(typeof(JsonStringEnumConverter<FocusStrengthStatus>))]
    public FocusStrengthStatus Status { get; set; } = FocusStrengthStatus.Unknown;

    public int Wins { get; set; }

    public int Games { get; set; }

    public double? HeroMmr { get; set; }

    public string RankTier { get; set; }

    public DateTime? FetchedAtUtc { get; set; }

    /// <summary>Wins over games, 0 to 1; null without games.</summary>
    [JsonIgnore]
    public double? WinRate => Games <= 0 ? null : (double)Math.Clamp(Wins, 0, Games) / Games;

    [JsonIgnore]
    public bool IsKnown => Status == FocusStrengthStatus.Known;
}
