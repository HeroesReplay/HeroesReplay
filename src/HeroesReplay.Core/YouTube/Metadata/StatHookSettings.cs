namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>
/// Title hooks from Heroes Profile hero statistics (<c>YouTube:Titles:StatHooks</c>, issue #272).
/// <see cref="Enabled"/> also turns on the download role's refresh of the statistics files.
/// Rates and points are percent. A hook takes the draft note's title slot when the match has no
/// draft note; its numbers go on a <c>Stats:</c> description line.
/// </summary>
public class StatHookSettings
{
    public bool Enabled { get; set; }

    public bool Counters { get; set; } = true;
    public bool Duos { get; set; } = true;
    public bool BestMap { get; set; } = true;
    public bool SlipsTheBan { get; set; } = true;
    public bool WorstMap { get; set; } = true;
    public bool PatchExtremes { get; set; } = true;

    /// <summary><c>A counters B</c>: games of A against B.</summary>
    public int CounterMinGames { get; set; } = 250;

    /// <summary><c>A counters B</c>: A's win rate against B.</summary>
    public double CounterMinWinRate { get; set; } = 57;

    /// <summary>
    /// <c>A counters B</c>: points A's rate against B must clear what their overall rates predict.
    /// The low end of its 95% interval must also clear 50 and that prediction.
    /// </summary>
    public double CounterMinEdge { get; set; } = 4;

    /// <summary><c>A + B duo</c>: games together.</summary>
    public int DuoMinGames { get; set; } = 300;

    /// <summary><c>A + B duo</c>: win rate together.</summary>
    public double DuoMinWinRate { get; set; } = 56;

    /// <summary><c>A + B duo</c>: the low end of the 95% interval of that rate.</summary>
    public double DuoMinLowerBound { get; set; } = 52;

    /// <summary>Best and worst map: games a map needs to count.</summary>
    public int MapMinGames { get; set; } = 150;

    /// <summary>Best and worst map: points over or under the hero's overall rate.</summary>
    public double MapMinDelta { get; set; } = 3;

    /// <summary><c>Hero slips the ban</c>: ban rate on this map.</summary>
    public double BanMinRate { get; set; } = 40;

    /// <summary><c>Hero slips the ban</c>: the hero's games on this map.</summary>
    public int BanMinGames { get; set; } = 150;

    /// <summary>Underdog and powerhouse: the hero's games this patch, and every ranked hero's.</summary>
    public int ExtremesMinGames { get; set; } = 500;

    /// <summary>Underdog and powerhouse: how many heroes at each end of the patch's win rates.</summary>
    public int ExtremesCount { get; set; } = 3;
}
