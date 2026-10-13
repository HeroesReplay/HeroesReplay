using System;

namespace HeroesReplay.Core.Analysis.Insights;

/// <summary>
/// The thresholds behind the match insights (#412), bound at <c>Insights</c>. The defaults are
/// the values measured on 101 Storm League and Quick Match games of build 2.57.0.98304.
/// </summary>
public class InsightsSettings
{
    /// <summary>The unit names of a team's core: the King's Core, and Alterac Pass's two generals.</summary>
    public string[] CoreUnits { get; set; } = { "KingsCore", "VanndarStormpike", "DrekThar" };

    /// <summary>A hero position older than this is not used.</summary>
    public TimeSpan MaxPositionAge { get; set; } = TimeSpan.FromSeconds(4);

    public LaneInsightSettings Lanes { get; set; } = new();

    public StandoutSettings Standouts { get; set; } = new();

    public CampInsightSettings Camps { get; set; } = new();

    public FinishInsightSettings Finish { get; set; } = new();

    public ComebackInsightSettings Comeback { get; set; } = new();

    public BuildInsightSettings Builds { get; set; } = new();
}

/// <summary>
/// An insane comeback: the team whose core stood at the end had lost clearly more buildings
/// (towers, forts and keeps) than the team whose core fell, or trailed by levels during the match.
/// </summary>
public class ComebackInsightSettings
{
    /// <summary>The surviving team lost at least this many more towers, forts and keeps.</summary>
    public int MinBuildingDeficit { get; set; } = 3;

    /// <summary>
    /// Or the surviving team was this many levels behind at some point. Two levels happen in 14% of
    /// games (a level-up a few seconds apart counts), so it takes three: with 3 buildings, 21 of 545
    /// games (4%).
    /// </summary>
    public int MinLevelDeficit { get; set; } = 3;

    /// <summary>Team levels are compared this often from gates open to the core.</summary>
    public TimeSpan SampleEvery { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Who soaks a side lane while the laning lasts. The window runs from gates open plus
/// <see cref="WindowStart"/> to the first team's level 10 (median 6.1 minutes after gates),
/// capped at <see cref="WindowCap"/>; after that 5v5 grouping hides the solo laner.
/// </summary>
public class LaneInsightSettings
{
    public TimeSpan WindowStart { get; set; } = TimeSpan.FromSeconds(45);

    public TimeSpan WindowCap { get; set; } = TimeSpan.FromMinutes(8);

    public TimeSpan SampleEvery { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Samples this close to the player's own core (at base) are skipped.</summary>
    public double BaseRadius { get; set; } = 22;

    /// <summary>A player is alone when no living teammate is this close.</summary>
    public double AloneRadius { get; set; } = 30;

    /// <summary>A soaker spends at least this share of their samples in one side lane.</summary>
    public double SoakerLaneShare { get; set; } = 0.45;

    /// <summary>A soaker is alone at least this share of their samples.</summary>
    public double SoakerAlone { get; set; } = 0.35;

    /// <summary>A player needs this many samples in the window to be judged.</summary>
    public int MinSamples { get; set; } = 30;

    /// <summary>Heroes never named as a solo laner: Abathur hats lanes from wherever he hides.</summary>
    public string[] ExcludedHeroes { get; set; } = { "Abathur" };
}

/// <summary>Positive individual performances only (#412): no call-out for a struggling player.</summary>
public class StandoutSettings
{
    /// <summary>No deaths and at least this many kills.</summary>
    public int DeathlessMinKills { get; set; } = 8;

    public int KillLeaderMinKills { get; set; } = 10;

    public int KillLeaderMaxDeaths { get; set; } = 1;

    /// <summary>At least this many kills, and <see cref="TeamKillFactor"/> times every teammate's.</summary>
    public int TeamKillMinKills { get; set; } = 8;

    public double TeamKillFactor { get; set; } = 2;
}

public class CampInsightSettings
{
    /// <summary>A camp is on a team's side when it is nearer that team's core by at least this ratio.</summary>
    public double SideRatio { get; set; } = 0.85;

    /// <summary>A drought: a team took at most this many camps.</summary>
    public int DroughtMaxCaptures { get; set; } = 1;

    /// <summary>A drought needs a match at least this long after gates open.</summary>
    public TimeSpan DroughtMinLength { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// A core race: both teams lost keeps, and the team whose core fell still had heroes at the
/// other core in the final moments. Core health is not in the replay, so this is inferred.
/// </summary>
public class FinishInsightSettings
{
    public int MinKeepsLostEach { get; set; } = 1;

    public double CoreRadius { get; set; } = 30;

    public TimeSpan FinalWindow { get; set; } = TimeSpan.FromSeconds(60);

    public int MinAttackers { get; set; } = 2;
}

/// <summary>How a player's talents are matched to a guide build.</summary>
public class BuildInsightSettings
{
    public int MinTiers { get; set; } = 4;

    public double SituationalWeight { get; set; } = 0.5;

    public double AmbiguityMargin { get; set; } = 0.10;
}
