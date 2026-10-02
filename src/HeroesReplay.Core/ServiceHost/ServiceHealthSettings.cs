using System;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// How often each role refreshes its heartbeat, and how long <c>services status</c> waits before
/// it calls a live role stale or degraded. Bound from the <c>ServiceHealth</c> section.
/// </summary>
public sealed class ServiceHealthSettings
{
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);
    public const int DefaultStaleAfterIntervals = 3;

    public TimeSpan HeartbeatInterval { get; set; } = DefaultHeartbeatInterval;

    /// <summary>A live role whose heartbeat is older than this many intervals is stale.</summary>
    public int StaleAfterIntervals { get; set; } = DefaultStaleAfterIntervals;

    /// <summary>
    /// A spectate tick: the HUD clock advanced, or one pass of the replay loop ended (a match,
    /// an idle wait, a held client, or an outage pause).
    /// </summary>
    public TimeSpan SpectateWorkThreshold { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>A Twitch reconcile: one pass of the prediction watcher, about every second.</summary>
    public TimeSpan TwitchWorkThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>A download pass: one Heroes Profile download attempt, every 2 to 15 seconds.</summary>
    public TimeSpan DownloadWorkThreshold { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>An upload pass: the uploader's pending drain, or its one-minute poll.</summary>
    public TimeSpan YouTubeWorkThreshold { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan Interval =>
        HeartbeatInterval > TimeSpan.Zero ? HeartbeatInterval : DefaultHeartbeatInterval;

    /// <summary>
    /// The heartbeat age at which a live role is stale. The role's own interval wins when its
    /// heartbeat names one, so a status read with other settings still judges it fairly.
    /// </summary>
    public TimeSpan StaleAfter(int? roleIntervalSeconds)
    {
        TimeSpan interval =
            roleIntervalSeconds is int seconds && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : Interval;
        int intervals = StaleAfterIntervals > 0 ? StaleAfterIntervals : DefaultStaleAfterIntervals;
        return interval * intervals;
    }

    public TimeSpan WorkThreshold(string role)
    {
        TimeSpan threshold = role?.ToLowerInvariant() switch
        {
            "spectate" => SpectateWorkThreshold,
            "twitch" => TwitchWorkThreshold,
            "download" => DownloadWorkThreshold,
            "youtube" => YouTubeWorkThreshold,
            _ => TimeSpan.Zero,
        };
        return threshold > TimeSpan.Zero ? threshold : TimeSpan.FromMinutes(30);
    }

    /// <summary>What counts as successful work for the role, for causes a person reads.</summary>
    public static string WorkName(string role) =>
        role?.ToLowerInvariant() switch
        {
            "spectate" => "spectate tick",
            "twitch" => "Twitch reconcile",
            "download" => "download pass",
            "youtube" => "upload pass",
            _ => "work",
        };
}
