using System;

namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>
/// Title stories from the players' Heroes Profile match history (<c>YouTube:Titles:PlayerStories</c>):
/// a win or loss streak, an MMR climb or slide, a one-trick, the player's best or worst hero lately.
/// <see cref="Enabled"/> also turns on the download role's history lookups for waiting focus cards.
/// A story only ever counts games played before the match, names the hero and never the player,
/// and its numbers go on a description line.
/// </summary>
public class PlayerStorySettings
{
    public bool Enabled { get; set; }

    public bool Streaks { get; set; } = true;
    public bool Climbs { get; set; } = true;
    public bool OneTricks { get; set; } = true;
    public bool BestHero { get; set; } = true;
    public bool WorstHero { get; set; } = true;

    /// <summary>Wins or losses in a row, ending with the game before this one.</summary>
    public int StreakMinGames { get; set; } = 5;

    /// <summary>
    /// MMR gained or lost over the last <see cref="MmrGames"/> games. Heroes Profile moves a
    /// settled Storm League MMR a median 3.6 points a game, 7.5 at the 90th percentile (1,176
    /// games, measured 2026-11-22), so 20 coin-flip games drift about 20 points: 50 is a real run.
    /// </summary>
    public double ClimbMinMmr { get; set; } = 50;

    /// <summary>How many of the latest games a climb or slide adds up.</summary>
    public int MmrGames { get; set; } = 20;

    /// <summary>A climb or slide needs at least this many games with an MMR change.</summary>
    public int ClimbMinGames { get; set; } = 10;

    /// <summary>
    /// A game that moved MMR more than this is a placement or a fresh account (seen: +780 over 6
    /// games), so a window holding one tells no climb.
    /// </summary>
    public double ClimbMaxGameMmr { get; set; } = 60;

    /// <summary>One-trick: the share of the latest games on this hero, 0 to 1.</summary>
    public double OneTrickMinShare { get; set; } = 0.7;

    /// <summary>One-trick: the latest games it needs at all.</summary>
    public int OneTrickMinGames { get; set; } = 20;

    /// <summary>
    /// One-trick by career: the player's Storm League games on the hero, from the focus record
    /// (<c>players/heroes</c>), when it is known.
    /// </summary>
    public int MainMinCareerGames { get; set; } = 300;

    /// <summary>Best or worst hero lately: games a hero needs in the history to be compared.</summary>
    public int LatelyMinGames { get; set; } = 8;

    /// <summary>Best hero lately: its win rate in the history, 0 to 1.</summary>
    public double BestMinWinRate { get; set; } = 0.6;

    /// <summary>Worst hero lately: its win rate in the history, 0 to 1.</summary>
    public double WorstMaxWinRate { get; set; } = 0.4;

    /// <summary>How far back the history is read, from the match's date.</summary>
    public int LookbackDays { get; set; } = 60;

    /// <summary>History lookups per focus step (about 10 s each when Heroes Profile runs a job).</summary>
    public int LookupsPerStep { get; set; } = 10;

    /// <summary>The most history lookups in any 7 days (<c>players/matches</c> allows 50,000).</summary>
    public int WeeklyCallBudget { get; set; } = 10000;

    /// <summary>A failed or skipped lookup is tried again after this long.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromHours(6);
}
