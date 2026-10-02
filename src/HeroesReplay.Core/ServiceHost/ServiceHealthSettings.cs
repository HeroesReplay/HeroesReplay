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
    public const int DefaultSpectateNoProgressSessions = 3;

    public TimeSpan HeartbeatInterval { get; set; } = DefaultHeartbeatInterval;

    /// <summary>A live role whose heartbeat is older than this many intervals is stale.</summary>
    public int StaleAfterIntervals { get; set; } = DefaultStaleAfterIntervals;

    /// <summary>
    /// Match progress: the match clock advanced, or a replay session reached the clock or the
    /// award screen. A deferred, held, timed-out, or failed session, an idle wait, and an outage
    /// pause are not work.
    /// </summary>
    public TimeSpan SpectateWorkThreshold { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Spectate is degraded (<c>spectate.no_match_progress</c>) once this many replay sessions in
    /// a row end without match progress.
    /// </summary>
    public int SpectateNoProgressSessions { get; set; } = DefaultSpectateNoProgressSessions;

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

    /// <summary>
    /// Sessions in a row without match progress that make the role degraded. Zero for a role
    /// that has no replay sessions.
    /// </summary>
    public int NoProgressSessions(string role) =>
        string.Equals(role, "spectate", StringComparison.OrdinalIgnoreCase)
            ? SpectateNoProgressSessions > 0
                ? SpectateNoProgressSessions
                : DefaultSpectateNoProgressSessions
            : 0;

    /// <summary>What counts as successful work for the role, for causes a person reads.</summary>
    public static string WorkName(string role) =>
        role?.ToLowerInvariant() switch
        {
            "spectate" => "match progress",
            "twitch" => "Twitch reconcile",
            "download" => "download pass",
            "youtube" => "upload pass",
            _ => "work",
        };
}
