using System;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// The hero statistics the download role keeps for YouTube title hooks
/// (<c>HeroesProfileApi:HeroStats</c>). The switch is <c>YouTube:Titles:StatHooks:Enabled</c>.
/// One file per major patch and game type, under <c>Data\HeroesProfile\hero-stats</c>.
/// </summary>
public class HeroStatsSettings
{
    public static readonly string[] DefaultGameTypes = new[] { "Storm League" };

    /// <summary>
    /// The game types to keep, by Heroes Profile name or short code. Null means
    /// <see cref="DefaultGameTypes"/>; a bound array would otherwise add to a default one.
    /// </summary>
    public string[] GameTypes { get; set; }

    public string[] GameTypeList() => GameTypes ?? DefaultGameTypes;

    /// <summary>A file older than this is fetched again.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>A file older than this is stale: titles do not use it.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(72);

    /// <summary>How often the download role checks whether a refresh is due.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long one job may run before the refresh gives up on it.</summary>
    public TimeSpan JobTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>The least time between two requests. The API allows 60 a minute, polls included.</summary>
    public TimeSpan RequestSpacing { get; set; } = TimeSpan.FromMilliseconds(1100);

    /// <summary>The least time between two <c>group_by_map</c> calls, which the API allows once a minute.</summary>
    public TimeSpan GroupByMapSpacing { get; set; } = TimeSpan.FromSeconds(61);
}
