using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Configuration;

public class HeroesProfileApiSettings
{
    public Uri BaseUri { get; set; }
    public Uri TwitchBaseUri { get; set; }
    public string ApiKey { get; set; }
    public string AwsAccessKey { get; set; }
    public string AwsSecretKey { get; set; }
    public IEnumerable<string> GameTypes { get; set; }
    public string S3Bucket { get; set; }
    public string S3Region { get; set; }
    public IEnumerable<string> ReplayUrlHostContains { get; set; }
    public int MinReplayId { get; set; }
    public int FallbackMaxReplayId { get; set; }
    public int ApiMaxReturnedReplays { get; set; }
    public bool EnableMMR { get; set; }
    public TimeSpan APIRetryWaitTime { get; set; }
    public string StandardCacheDirectoryName { get; set; }
    public string RequestsCacheDirectoryName { get; set; }

    public bool MatchesReplayUrl(Uri url)
    {
        if (url == null)
            return false;

        IEnumerable<string> needles = (ReplayUrlHostContains ?? Enumerable.Empty<string>())
            .Append(S3Bucket)
            .Where(n => !string.IsNullOrWhiteSpace(n));

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
