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
