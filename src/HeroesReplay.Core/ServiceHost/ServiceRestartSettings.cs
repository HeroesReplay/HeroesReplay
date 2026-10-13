using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Obs;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// How the opt-in supervisor (<c>services start --supervise</c>, <c>services supervise</c>)
/// restarts roles. Bound from the <c>ServiceRestart</c> section.
/// </summary>
public sealed class ServiceRestartSettings
{
    public static readonly IReadOnlyList<TimeSpan> DefaultBackoff = new[]
    {
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
    };

    public const int DefaultBudget = 5;
    public static readonly TimeSpan DefaultBudgetWindow = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DefaultStaleRestartAfter = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DefaultReadyTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan DefaultSlowReadyTimeout = TimeSpan.FromMinutes(3);
    public const double DefaultSlowReadyCommitPercent = 90;

    /// <summary>
    /// The wait before restart n, where n counts the restarts still inside the budget window.
    /// The last delay repeats. Null or empty means 10 s, 30 s, 2 min, 5 min.
    /// </summary>
    public List<TimeSpan> Backoff { get; set; }

    /// <summary>Restarts allowed per role inside <see cref="BudgetWindow"/>.</summary>
    public int Budget { get; set; } = DefaultBudget;

    public TimeSpan BudgetWindow { get; set; } = DefaultBudgetWindow;

    /// <summary>
    /// A live role whose heartbeat is this old is killed, then restarted like a failed role.
    /// Kept above the stale limit in <c>ServiceHealth</c> (3 heartbeat intervals).
    /// </summary>
    public TimeSpan StaleRestartAfter { get; set; } = DefaultStaleRestartAfter;

    /// <summary>How often the supervisor reads the roles and the stop file.</summary>
    public TimeSpan PollInterval { get; set; } = DefaultPollInterval;

    /// <summary>
    /// What happens to a live stream this install started when spectate used its restart budget
    /// and stays down: show the waiting scene (default), stop the stream, or nothing.
    /// </summary>
    public ObsFailSafeAction SpectateDownObs { get; set; } = ObsFailSafeAction.WaitingScene;

    /// <summary>How long a restart waits for the new role's ready file and first heartbeat.</summary>
    public TimeSpan ReadyTimeout { get; set; } = DefaultReadyTimeout;

    /// <summary>
    /// The ready wait of a restart while the machine is short of memory: the commit charge is
    /// above <see cref="SlowReadyCommitPercent"/> (#397). A role that thrashes on start can take
    /// minutes to write its ready file, and the restart waits for it rather than give up.
    /// </summary>
    public TimeSpan SlowReadyTimeout { get; set; } = DefaultSlowReadyTimeout;

    /// <summary>Commit charge, percent of the commit limit, above which the slow wait applies.</summary>
    public double SlowReadyCommitPercent { get; set; } = DefaultSlowReadyCommitPercent;

    /// <summary>
    /// The supervisor watches OBS (#398): it starts OBS when the process is gone and restarts it
    /// when its websocket hangs, only while streaming is desired here (<c>OBS:Enabled</c>,
    /// <c>OBS:StreamingEnabled</c>, and the machine's stream arm). Off by default and in dev; on
    /// in prod.
    /// </summary>
    public bool ObsWatchdog { get; set; }

    /// <summary>
    /// The wait before OBS start n + 1 inside <see cref="BudgetWindow"/>: the first start is at
    /// once, then 1, 2, 5, 10 min (the last repeats). Null or empty means those.
    /// </summary>
    public List<TimeSpan> ObsBackoff { get; set; }

    /// <summary>OBS starts and restarts allowed inside <see cref="BudgetWindow"/>. Default 4.</summary>
    public int ObsBudget { get; set; } = ObsWatchdogRules.DefaultBudget;

    /// <summary>How long a hung OBS gets to close after <c>CloseMainWindow</c> before it is killed.</summary>
    public TimeSpan ObsCloseWait { get; set; } = ObsWatchdogRules.DefaultCloseWait;

    /// <summary>How often the watchdog asks OBS's websocket for the stream status. Default 30 s.</summary>
    public TimeSpan ObsWatchdogInterval { get; set; } = ObsWatchdogRules.DefaultProbeInterval;

    /// <summary>The OBS watchdog's rules, with <see cref="OBSSettings.HungAfter"/> and the launch.</summary>
    public ObsWatchdogRules ObsRules(OBSSettings obs) =>
        new()
        {
            Enabled = ObsWatchdog,
            HungAfter =
                obs?.HungAfter > TimeSpan.Zero ? obs.HungAfter : ObsWatchdogRules.DefaultHungAfter,
            Backoff = ObsBackoff?.Where(delay => delay > TimeSpan.Zero).ToList()
                is { Count: > 0 } delays
                ? delays
                : ObsWatchdogRules.DefaultBackoff,
            Budget = ObsBudget > 0 ? ObsBudget : ObsWatchdogRules.DefaultBudget,
            Window = Window,
            CloseWait =
                ObsCloseWait > TimeSpan.Zero ? ObsCloseWait : ObsWatchdogRules.DefaultCloseWait,
            ProbeInterval =
                ObsWatchdogInterval > TimeSpan.Zero
                    ? ObsWatchdogInterval
                    : ObsWatchdogRules.DefaultProbeInterval,
            ExecutablePath = ObsLaunchDecision.ResolveExecutable(obs?.ExecutablePath),
            Arguments = ObsLaunchDecision.ArgumentsFor(
                ObsNames.Profile(obs),
                ObsNames.SceneCollection(obs)
            ),
        };

    public TimeSpan ReadyWait => ReadyTimeout > TimeSpan.Zero ? ReadyTimeout : DefaultReadyTimeout;

    /// <summary>Never shorter than <see cref="ReadyWait"/>.</summary>
    public TimeSpan SlowReadyWait => SlowReadyTimeout > ReadyWait ? SlowReadyTimeout : ReadyWait;

    /// <summary>
    /// The ready wait for a restart when the commit charge is <paramref name="commitPercent"/>.
    /// An unreadable charge (null) gets the normal wait.
    /// </summary>
    public TimeSpan ReadyWaitFor(double? commitPercent) =>
        commitPercent is double percent && percent > SlowReadyCommitPercent
            ? SlowReadyWait
            : ReadyWait;

    public IReadOnlyList<TimeSpan> Delays
    {
        get
        {
            List<TimeSpan> configured = Backoff?.Where(delay => delay > TimeSpan.Zero).ToList();
            return configured is { Count: > 0 } ? configured : DefaultBackoff;
        }
    }

    public int Limit => Budget > 0 ? Budget : DefaultBudget;

    public TimeSpan Window => BudgetWindow > TimeSpan.Zero ? BudgetWindow : DefaultBudgetWindow;

    public TimeSpan StaleLimit =>
        StaleRestartAfter > TimeSpan.Zero ? StaleRestartAfter : DefaultStaleRestartAfter;

    public TimeSpan Poll => PollInterval > TimeSpan.Zero ? PollInterval : DefaultPollInterval;

    /// <summary>The wait before the next restart when <paramref name="used"/> restarts are in the window.</summary>
    public TimeSpan BackoffFor(int used)
    {
        IReadOnlyList<TimeSpan> delays = Delays;
        return delays[Math.Clamp(used, 0, delays.Count - 1)];
    }
}
