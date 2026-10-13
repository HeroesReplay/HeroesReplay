using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.HeroesProfile;

public class HeroesProfileApiSettings
{
    public Uri ExternalV1BaseUri { get; set; }
    public Uri TwitchBaseUri { get; set; }
    public string ApiKey { get; set; }
    public IEnumerable<string> GameTypes { get; set; }
    public IEnumerable<string> ReplayUrlHostContains { get; set; }
    public int MinReplayId { get; set; }
    public int FallbackMaxReplayId { get; set; }
    public int ApiMaxReturnedReplays { get; set; }

    /// <summary>
    /// The downloader stops while this many Standard replays inside their media window are
    /// waiting. A waiting replay past its window (<c>ReplayMedia</c>, judged by game date) does
    /// not count, so a backlog cannot block fresh downloads (#280).
    /// </summary>
    public int CachedReplayLimit { get; set; } = 5;

    /// <summary>A Standard candidate older than this makes the cursor jump to recent ids. Zero turns it off.</summary>
    public TimeSpan StandardMaxReplayAge { get; set; } = TimeSpan.FromHours(12);

    /// <summary>How many ids below the newest Heroes Profile id the cursor jumps to.</summary>
    public int StandardCatchUpWindow { get; set; } = 3000;
    public TimeSpan APIRetryWaitTime { get; set; }
    public string StandardCacheDirectoryName { get; set; }
    public string RequestsCacheDirectoryName { get; set; }

    /// <summary>
    /// How long the saved <c>/maps</c> list (<c>Data\HeroesProfile\maps.json</c>) is reused before
    /// a reward sync fetches it again (#425). <c>/maps</c> allows 1,000,000 calls a week (measured
    /// 2026-10-13). Zero turns the fetch off; a saved list is still read.
    /// </summary>
    public TimeSpan MapsRefreshInterval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// How many replay ids back a rank reward's search reads Heroes Profile's list (#429). One
    /// call returns up to 1,000 games of that type and map after it, so a rank reward gets more
    /// candidates than a map reward, whose search reads <see cref="ApiMaxReturnedReplays"/> ids.
    /// </summary>
    public int RankSearchWindow { get; set; } = 20000;

    /// <summary>
    /// How many candidates a rank reward looks up before its points go back (#429). Each costs a
    /// <c>/replay/{id}</c> call (500 a minute) and, unless that MMR is cached, a <c>/mmr/tier</c>
    /// call (60 a minute). Ranks are uneven: 25 recent Storm League games on Cursed Hollow on
    /// 2026-10-13 were 11 Bronze, 7 Platinum, 3 Silver, 2 Gold, 2 Master and no Diamond.
    /// </summary>
    public int RankLookupLimit { get; set; } = 40;

    /// <summary>How long a rank reward's lookups may take in all before its points go back (#429).</summary>
    public TimeSpan RankSearchTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The per-patch hero statistics behind YouTube title hooks.</summary>
    public HeroStatsSettings HeroStats { get; set; } = new HeroStatsSettings();

    public bool MatchesReplayUrl(Uri url)
    {
        if (url == null)
            return false;

        IEnumerable<string> needles = (ReplayUrlHostContains ?? Enumerable.Empty<string>()).Where(
            n => !string.IsNullOrWhiteSpace(n)
        );

        return needles.Any(n =>
            url.Host.Contains(n, StringComparison.OrdinalIgnoreCase)
            || url.AbsolutePath.Contains(n, StringComparison.OrdinalIgnoreCase)
        );
    }

    public bool IsAllowedGameType(string gameType)
    {
        if (string.IsNullOrWhiteSpace(gameType))
            return false;

        IEnumerable<string> allowed = GameTypes ?? Enumerable.Empty<string>();
        if (!allowed.Any())
        {
            return IsStormLeague(gameType);
        }

        return allowed.Any(a => GameTypesEqual(a, gameType));
    }

    private static bool IsStormLeague(string gameType) => GameTypesEqual("Storm League", gameType);

    /// <summary>
    /// True when <paramref name="actual"/> is the game type <paramref name="expected"/> names
    /// ("Storm League" and "sl" are the same). False when either is empty.
    /// </summary>
    public static bool SameGameType(string expected, string actual) =>
        !string.IsNullOrWhiteSpace(expected)
        && !string.IsNullOrWhiteSpace(actual)
        && GameTypesEqual(expected, actual);

    private static bool GameTypesEqual(string expected, string actual)
    {
        if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string a = NormalizeGameType(expected);
        string b = NormalizeGameType(actual);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeGameType(string gameType)
    {
        if (
            string.Equals(gameType, "sl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(gameType, "Storm League", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "sl";
        }

        return gameType;
    }
}
