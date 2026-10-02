using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.Core.SelfUpdate;

/// <summary>What the post-install health gate decided. The value is the exit code.</summary>
public enum ReleaseHealthVerdict
{
    /// <summary>Every recorded role heartbeats from the new install and spectate showed match progress.</summary>
    Healthy = 0,

    /// <summary>Not healthy yet. The stabilization window is still open.</summary>
    Waiting = 1,

    /// <summary>The window closed before the install was healthy. Roll back.</summary>
    Unhealthy = 2,

    /// <summary>A stop was requested. The gate does not judge a stack someone is stopping.</summary>
    Stopped = 3,
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
/// match clock, or the award screen). Anything short of that when the window closes is
/// unhealthy, and the script restores <c>.previous</c>.
/// </summary>
public static class ReleaseHealth
{
    /// <summary>Long enough for the client to start, one replay to load, and the clock to tick, with a retry.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(20);

    public const string Spectate = "spectate";

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
        if (expected.Count == 0)
        {
            problems.Add("services.json lists no roles, so the stack is not running.");
        }
        else if (!expected.Any(IsSpectate))
        {
            problems.Add("spectate is not in services.json, so match progress cannot be seen.");
        }

        foreach (ServiceRoleHealth role in expected)
        {
            string problem = RoleProblem(role, since);
            if (problem == null && IsSpectate(role))
            {
                problem = MatchProgressProblem(role, since);
            }

            if (problem != null)
            {
                problems.Add(problem);
            }
        }

        if (problems.Count == 0)
        {
            return new ReleaseHealthResult(ReleaseHealthVerdict.Healthy, problems);
        }

        bool open = now - since < Window(window);
        return new ReleaseHealthResult(
            open ? ReleaseHealthVerdict.Waiting : ReleaseHealthVerdict.Unhealthy,
            problems
        );
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

    private static string MatchProgressProblem(ServiceRoleHealth spectate, DateTimeOffset since)
    {
        if (spectate.LastSuccessfulWorkAt is DateTimeOffset work && work >= since)
        {
            return null;
        }

        string sessions =
            spectate.SessionsWithoutProgress is int misses && misses > 0
                ? $" ({misses} sessions without it, the last {spectate.LastOutcome ?? "unknown"})"
                : string.Empty;
        return $"spectate has shown no match progress since the install{sessions}.";
    }

    private static bool IsSpectate(ServiceRoleHealth role) =>
        string.Equals(role.Role, Spectate, StringComparison.OrdinalIgnoreCase);

    private static string Describe(long seconds) =>
        ServiceHealthClassifier.Describe(TimeSpan.FromSeconds(seconds));
}
