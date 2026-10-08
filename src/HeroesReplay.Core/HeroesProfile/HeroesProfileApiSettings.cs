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
