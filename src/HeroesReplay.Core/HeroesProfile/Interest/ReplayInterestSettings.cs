using System;

namespace HeroesReplay.Core.HeroesProfile.Interest;

/// <summary>
/// <c>HeroesProfileApi:Interest</c> (#510). While enabled, the download role reads the score
/// screen of a page's first <see cref="Candidates"/> current-patch replays from Heroes Profile
/// and downloads the one <see cref="ReplayInterest"/> scores highest, instead of the lowest id.
/// The points are relative: they only rank candidates against each other.
/// </summary>
public sealed class ReplayInterestSettings
{
    public bool Enabled { get; set; }

    /// <summary>How many listed candidates are scored for one download. Each is a <c>/replay/{id}</c> call (500 a minute).</summary>
    public int Candidates { get; set; } = 20;

    /// <summary>How long one candidate's detail may take before the pick goes on without it.</summary>
    public TimeSpan DetailTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>A player with no deaths and at least this many kills is a deathless carry.</summary>
    public int DeathlessCarryKills { get; set; } = 8;

    public int DeathlessCarryPoints { get; set; } = 30;

    /// <summary>A player with at least this many kills and at most one death leads the kills.</summary>
    public int KillLeaderKills { get; set; } = 10;

    public int KillLeaderPoints { get; set; } = 20;

    /// <summary>A takedown streak this long without dying.</summary>
    public int LongStreak { get; set; } = 25;

    public int LongStreakPoints { get; set; } = 10;

    /// <summary>Seconds a player was on fire.</summary>
    public int OnFireSeconds { get; set; } = 300;

    public int OnFirePoints { get; set; } = 10;

    /// <summary>A team wipe: a takedown burst of five, in which every hero of the winning side counts.</summary>
    public int TeamWipeMultikill { get; set; } = 5;

    public int TeamWipePoints { get; set; } = 5;

    /// <summary>A game at least this long.</summary>
    public TimeSpan LongGame { get; set; } = TimeSpan.FromMinutes(25);

    public int LongGamePoints { get; set; } = 10;

    /// <summary>A game shorter than this is a stomp.</summary>
    public TimeSpan ShortGame { get; set; } = TimeSpan.FromMinutes(13);

    public int ShortGamePoints { get; set; } = -15;

    /// <summary>A Storm League game at this league or higher (Diamond, Master, Grandmaster).</summary>
    public string HighRank { get; set; } = "Diamond";

    public int HighRankPoints { get; set; } = 10;
}
