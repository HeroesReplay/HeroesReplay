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

    public static readonly TimeSpan DefaultSpectateLaunchStallThreshold = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Spectate is degraded (<c>spectate.launch_stalled</c>) and the supervisor restarts it once
    /// one replay's launch and loading phase has gone this long without match progress. The
    /// report, a hold, an empty queue, and an outage are not that phase; a client downloading or
    /// preparing game data starts it over. Zero or less means the default, 20 minutes.
    /// </summary>
    public TimeSpan SpectateLaunchStallThreshold { get; set; } =
        DefaultSpectateLaunchStallThreshold;

    /// <summary>A Twitch reconcile: one pass of the prediction watcher, about every second.</summary>
    public TimeSpan TwitchWorkThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>A download pass: one Heroes Profile download attempt, every 2 to 15 seconds.</summary>
    public TimeSpan DownloadWorkThreshold { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>An upload pass: the uploader's pending drain, or its one-minute poll.</summary>
    public TimeSpan YouTubeWorkThreshold { get; set; } = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan DefaultDependencyProbeInterval = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DefaultDependencyRetryInterval = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan DefaultDependencyProbeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>No probe repeats faster than this, whatever the settings say.</summary>
    public static readonly TimeSpan MinimumDependencyProbeInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Each role checks its live dependency once before it reports ready and then every
    /// <see cref="DependencyProbeInterval"/> (#305). False turns every probe off.
    /// </summary>
    public bool DependencyProbes { get; set; } = true;

    /// <summary>The time between probes while the dependency is ok. At least one minute.</summary>
    public TimeSpan DependencyProbeInterval { get; set; } = DefaultDependencyProbeInterval;

    /// <summary>
    /// The time between probes while the dependency fails, so a fixed key or an outage that ends
    /// clears the degraded state sooner. At least one minute.
    /// </summary>
    public TimeSpan DependencyRetryInterval { get; set; } = DefaultDependencyRetryInterval;

    /// <summary>How long one probe may take before it counts as unreachable. 1 to 60 seconds.</summary>
    public TimeSpan DependencyProbeTimeout { get; set; } = DefaultDependencyProbeTimeout;

    /// <summary>
    /// Spectate's probe: identify on the OBS websocket with Get requests only, while OBS runs.
    /// Off until the AGENTS.md short live proof has run with it on.
    /// </summary>
    public bool SpectateObsProbe { get; set; }

    /// <summary>The wait before the next probe: shorter after a failure, never under a minute.</summary>
    public TimeSpan NextDependencyProbe(bool failed)
    {
        TimeSpan configured = failed ? DependencyRetryInterval : DependencyProbeInterval;
        TimeSpan fallback = failed
            ? DefaultDependencyRetryInterval
            : DefaultDependencyProbeInterval;
        TimeSpan wait = configured > TimeSpan.Zero ? configured : fallback;
        return wait < MinimumDependencyProbeInterval ? MinimumDependencyProbeInterval : wait;
    }

    /// <summary>The bound on one probe, between 1 and 60 seconds.</summary>
    public TimeSpan DependencyProbeBound()
    {
        if (DependencyProbeTimeout <= TimeSpan.Zero)
        {
            return DefaultDependencyProbeTimeout;
        }

        if (DependencyProbeTimeout < TimeSpan.FromSeconds(1))
        {
            return TimeSpan.FromSeconds(1);
        }

        return DependencyProbeTimeout > TimeSpan.FromSeconds(60)
            ? TimeSpan.FromSeconds(60)
            : DependencyProbeTimeout;
    }

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

    /// <summary>
    /// How long one launch may go without match progress before the role is stalled. Zero for a
    /// role that has no launch phase.
    /// </summary>
    public TimeSpan LaunchStallThreshold(string role) =>
        string.Equals(role, "spectate", StringComparison.OrdinalIgnoreCase)
            ? SpectateLaunchStallThreshold > TimeSpan.Zero
                ? SpectateLaunchStallThreshold
                : DefaultSpectateLaunchStallThreshold
            : TimeSpan.Zero;

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
