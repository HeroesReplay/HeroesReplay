using System;
using System.IO;

namespace HeroesReplay.Core.HeroFocus;

/// <summary>
/// The 24/7 cycle's own focus-hero replays (#433), the <c>Focus</c> section: which player of a
/// waiting replay is worth a whole match on one hero, and how quality is weighed against variety.
/// </summary>
public sealed class HeroFocusSettings
{
    /// <summary>
    /// The switch. On, the download role keeps focus cards and player records for the waiting
    /// replays and keeps <see cref="ExtraCandidates"/> more of them.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Every this many Standard replays the spectator plays, one is a focus replay: 3 is two
    /// ordinary replays, then a focus. Viewer requests play first and are not counted.
    /// </summary>
    public int Every { get; set; } = 3;

    /// <summary>
    /// While a focus is not due, the best candidate is held back from the ordinary picks when its
    /// novelty is at least this (an uncovered hero or role), so the next focus slot can take it.
    /// </summary>
    public double ReserveNoveltyAt { get; set; } = 0.8;

    /// <summary>
    /// A pick that has not played this long after it was made (a stop, a release update, a crash)
    /// is abandoned, and its replay plays as an ordinary one. Until then a restart keeps it.
    /// </summary>
    public TimeSpan PickExpiry { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Waiting replays kept beyond <c>HeroesProfileApi:CachedReplayLimit</c>, so focus picks have a choice.</summary>
    public int ExtraCandidates { get; set; } = 5;

    /// <summary>How often the download role builds a card and looks up records.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Cards built per refresh at most, before the lookups: a cold lookup waits about 10 s for
    /// Heroes Profile's job, so the cards of a backlog come first.
    /// </summary>
    public int CardsPerStep { get; set; } = 5;

    /// <summary>Heroes Profile lookups per refresh at most.</summary>
    public int StrengthLookupsPerStep { get; set; } = 10;

    /// <summary>A record that could not be read is asked for again after this long.</summary>
    public TimeSpan StrengthRetryAfter { get; set; } = TimeSpan.FromHours(1);

    /// <summary>A hero is not focused again until this many other focus picks have played.</summary>
    public int HeroRepeatWindow { get; set; } = 3;

    /// <summary>The same player is not focused again within this time.</summary>
    public TimeSpan PlayerCooldown { get; set; } = TimeSpan.FromDays(7);

    /// <summary>The newest focus picks the role, map, league and region shares are counted over.</summary>
    public int RecentWindow { get; set; } = 36;

    /// <summary>A covered hero's novelty returns in full this long after its last focus.</summary>
    public TimeSpan HeroRecovery { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Storm League games on the hero a player needs on Heroes Profile.</summary>
    public int MinHeroGames { get; set; } = 20;

    /// <summary>The win rate on the hero a player needs, 0 to 1.</summary>
    public double MinWinRate { get; set; } = 0.55;

    /// <summary>A player whose record is unknown (private, not looked up) may still be picked.</summary>
    public bool AllowUnknownStrength { get; set; } = true;

    /// <summary>The strength an unknown record counts as, 0 to 1.</summary>
    public double UnknownStrength { get; set; } = 0.3;

    /// <summary>A candidate below this quality is never picked, however new its hero is.</summary>
    public double QualityFloor { get; set; } = 0.45;

    /// <summary>(Kills + assists) per death that counts as a perfect KDA.</summary>
    public double KdaCap { get; set; } = 8;

    /// <summary>A game shorter than this counts as a stomp.</summary>
    public TimeSpan ShortGame { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>The newest values kept per hero and stat for the baselines.</summary>
    public int BaselineWindow { get; set; } = 300;

    /// <summary>Values a hero needs before it is judged against itself, not its role.</summary>
    public int BaselineMinSamples { get; set; } = 20;

    public HeroFocusWeights Weights { get; set; } = new();
}

/// <summary>How the parts of a candidate's score are weighed. Each group sums to 1.</summary>
public sealed class HeroFocusWeights
{
    public QualityWeights Quality { get; set; } = new();
    public PerformanceWeights Performance { get; set; } = new();
    public NoveltyWeights Novelty { get; set; } = new();
}

public sealed class QualityWeights
{
    public double Performance { get; set; } = 0.40;
    public double Strength { get; set; } = 0.35;
    public double Build { get; set; } = 0.15;
    public double Game { get; set; } = 0.10;
}

public sealed class PerformanceWeights
{
    public double Signature { get; set; } = 0.35;
    public double Kda { get; set; } = 0.25;
    public double Participation { get; set; } = 0.15;
    public double Awards { get; set; } = 0.15;
    public double Won { get; set; } = 0.10;
}

public sealed class NoveltyWeights
{
    public double Hero { get; set; } = 0.35;
    public double Role { get; set; } = 0.20;
    public double Build { get; set; } = 0.15;
    public double Map { get; set; } = 0.10;
    public double League { get; set; } = 0.08;
    public double Region { get; set; } = 0.05;
    public double Comp { get; set; } = 0.07;
}

/// <summary>Where the focus files live: <c>Data\Focus</c>.</summary>
public static class HeroFocusPaths
{
    public const string FolderName = "Focus";

    public static string FolderFor(string dataDirectory) => Path.Combine(dataDirectory, FolderName);
}
