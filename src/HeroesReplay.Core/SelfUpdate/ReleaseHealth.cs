using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Spectating.Session;

namespace HeroesReplay.Core.SelfUpdate;

/// <summary>What the post-install health gate decided. The value is the exit code.</summary>
public enum ReleaseHealthVerdict
{
    /// <summary>Every recorded role heartbeats from the new install and spectate showed match progress.</summary>
    Healthy = 0,

    /// <summary>Not healthy yet. The stabilization window is still open.</summary>
    Waiting = 1,

    /// <summary>
    /// The window closed and the build is at fault: a role is down or stale, or spectate tried a
    /// replay and none reached a match clock. Roll back.
    /// </summary>
    Unhealthy = 2,

    /// <summary>A stop was requested. The gate does not judge a stack someone is stopping.</summary>
    Stopped = 3,

    /// <summary>
    /// The window closed with every role up, but spectate had nothing it could play: no replay,
    /// or only replays held for reasons outside the build. Keep the install; do not roll back.
    /// </summary>
    Inconclusive = 4,
}

public sealed record ReleaseHealthResult(
    ReleaseHealthVerdict Verdict,
    IReadOnlyList<string> Problems
)
{
    public int ExitCode => (int)Verdict;

    public string Describe() =>
        Problems.Count == 0
            ? Verdict.ToString().ToLowerInvariant() + "."
            : Verdict.ToString().ToLowerInvariant() + ": " + string.Join(" ", Problems);
}

/// <summary>
/// The health gate <c>apply-release.ps1</c> runs after it installs a release
/// (<c>update release-health</c>). Healthy means every role in <c>services.json</c> runs, has a
/// fresh <c>ready/&lt;nonce&gt;.json</c> heartbeat, and became ready after the install, and
/// spectate recorded match progress after the install (its <c>lastSuccessfulWorkAt</c>: the
/// match clock, or the award screen). When the window closes without that, the build is blamed
/// only for what it did: a role that is down or stale, or replay sessions that failed
/// (<see cref="BuildFaults"/>). An empty queue, an outage, or only held replays is
/// inconclusive, and the install stays.
/// </summary>
public static class ReleaseHealth
{
    /// <summary>Long enough for the client to start, one replay to load, and the clock to tick, with a retry.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(20);

    public const string Spectate = "spectate";

    /// <summary>
    /// Session outcomes that are the build's fault: no match clock (including a client that would
    /// not open the replay, which ends <c>LoadTimedOut</c>), a crash or hang, a session that ran to
    /// its end without a clock, or an exception from the launch.
    /// </summary>
    public static readonly IReadOnlySet<string> BuildFaults = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        nameof(MatchOutcome.LoadTimedOut),
        nameof(MatchOutcome.ClientCrashed),
        nameof(MatchOutcome.ClientHung),
        nameof(MatchOutcome.Canceled),
        ReplaySession.ErrorOutcome,
    };

    /// <summary>Replays held for reasons outside the build. Stopped and None count as neither.</summary>
    public static readonly IReadOnlySet<string> HeldOutside = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        nameof(MatchOutcome.BuildNotInstalled),
        nameof(MatchOutcome.VersionMismatch),
        nameof(MatchOutcome.RegionUnavailable),
    };

    public static TimeSpan Window(TimeSpan configured) =>
        configured > TimeSpan.Zero ? configured : DefaultWindow;

    /// <param name="roles">The roles as <c>services status</c> classifies them.</param>
    /// <param name="stopRequested"><c>services.stop</c> is down.</param>
    /// <param name="since">When the script started the new stack.</param>
    /// <param name="now">The check time.</param>
    /// <param name="window">How long after <paramref name="since"/> the install may take.</param>
    public static ReleaseHealthResult Judge(
        IEnumerable<ServiceRoleHealth> roles,
        bool stopRequested,
        DateTimeOffset since,
        DateTimeOffset now,
        TimeSpan window
    )
    {
        if (stopRequested)
        {
            return new ReleaseHealthResult(
                ReleaseHealthVerdict.Stopped,
                new[] { "A stop was requested (services.stop), so the install is not judged." }
            );
        }

        var problems = new List<string>();
        List<ServiceRoleHealth> expected =
            roles?.Where(role => role != null && role.Expected).ToList() ?? new();
        ServiceRoleHealth spectate = expected.FirstOrDefault(IsSpectate);
        if (expected.Count == 0)
        {
            problems.Add("services.json lists no roles, so the stack is not running.");
        }
        else if (spectate == null)
        {
            problems.Add("spectate is not in services.json, so match progress cannot be seen.");
        }

        foreach (ServiceRoleHealth role in expected)
        {
            string problem = RoleProblem(role, since);
            if (problem != null)
            {
                problems.Add(problem);
            }
        }

        bool progress = spectate?.LastSuccessfulWorkAt is DateTimeOffset work && work >= since;
        if (problems.Count == 0 && progress)
        {
            return new ReleaseHealthResult(ReleaseHealthVerdict.Healthy, problems);
        }

        if (now - since < Window(window))
        {
            if (!progress && spectate != null)
            {
                problems.Add(
                    $"spectate has shown no match progress since the install{Sessions(spectate)}."
                );
            }

            return new ReleaseHealthResult(ReleaseHealthVerdict.Waiting, problems);
        }

        if (problems.Count > 0)
        {
            return new ReleaseHealthResult(ReleaseHealthVerdict.Unhealthy, problems);
        }

        int failed = Count(spectate, BuildFaults);
        if (failed > 0)
        {
            return new ReleaseHealthResult(
                ReleaseHealthVerdict.Unhealthy,
                new[]
                {
                    $"spectate tried {failed} replay session(s) after the install and none reached a match clock{Sessions(spectate)}.",
                }
            );
        }

        int held = Count(spectate, HeldOutside);
        string nothing =
            held > 0
                ? $"spectate had no replay it could play: {held} session(s) were held for reasons outside the build{Sessions(spectate)}."
                : $"spectate had no replay to play after the install (an empty queue, or no connectivity){Sessions(spectate)}.";
        return new ReleaseHealthResult(ReleaseHealthVerdict.Inconclusive, new[] { nothing });
    }

    private static string RoleProblem(ServiceRoleHealth role, DateTimeOffset since)
    {
        if (!role.Running)
        {
            return $"{role.Role} is not running ({role.State.ToString().ToLowerInvariant()}).";
        }

        if (role.HeartbeatAgeSeconds is not long age)
        {
            return $"{role.Role} has no heartbeat.";
        }

        if (age > role.StaleAfterSeconds)
        {
            return $"{role.Role}'s heartbeat is {Describe(age)} old (limit {Describe(role.StaleAfterSeconds)}).";
        }

        if (!string.Equals(role.Readiness, ServiceReadiness.Ready, StringComparison.Ordinal))
        {
            return $"{role.Role} is {role.Readiness ?? "not ready"}.";
        }

        if (role.ReadyAt is not DateTimeOffset ready || ready < since)
        {
            return $"{role.Role} has not started since the install.";
        }

        return null;
    }

    // RoleProblem requires spectate to have started after the install, so its tally is the install's.
    private static int Count(ServiceRoleHealth spectate, IReadOnlySet<string> outcomes) =>
        spectate
            ?.SessionOutcomes?.Where(item => outcomes.Contains(item.Key))
            .Sum(item => item.Value)
        ?? 0;

    private static string Sessions(ServiceRoleHealth spectate)
    {
        if (spectate?.SessionOutcomes == null || spectate.SessionOutcomes.Count == 0)
        {
            return string.Empty;
        }

        IEnumerable<string> counts = spectate
            .SessionOutcomes.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key} {item.Value}");
        return " (" + string.Join(", ", counts) + ")";
    }

    private static bool IsSpectate(ServiceRoleHealth role) =>
        string.Equals(role.Role, Spectate, StringComparison.OrdinalIgnoreCase);

    private static string Describe(long seconds) =>
        ServiceHealthClassifier.Describe(TimeSpan.FromSeconds(seconds));
}
