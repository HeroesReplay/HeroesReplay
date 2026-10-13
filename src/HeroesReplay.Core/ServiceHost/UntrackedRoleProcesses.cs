using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// A live role process that <c>services.json</c> does not track, with its heartbeat when it
/// writes one (#397). The heartbeat carries the nonce the supervisor needs to watch it.
/// </summary>
public sealed record UntrackedRoleProcess(
    ServiceProcessRecord Process,
    ServiceReadyReport Heartbeat
)
{
    /// <summary>
    /// It wrote a ready file the supervisor can read by nonce, and says it is ready: the
    /// supervisor can take it over instead of starting a second one.
    /// </summary>
    public bool Adoptable =>
        Heartbeat != null
        && ServiceReadyFile.IsSafeNonce(Heartbeat.Nonce)
        && string.Equals(Heartbeat.Readiness, ServiceReadiness.Ready, StringComparison.Ordinal);

    /// <summary>The record <c>services.json</c> keeps once the supervisor took it over.</summary>
    public ServiceProcessRecord Adopt() =>
        new()
        {
            Name = Process.Name,
            Pid = Process.Pid,
            Arguments = Process.Arguments,
            ExecutablePath = Process.ExecutablePath ?? Heartbeat?.ExecutablePath,
            StartedAt = Process.StartedAt,
            Nonce = Heartbeat?.Nonce,
            Version = Heartbeat?.Version,
            ReadyAt = Heartbeat?.ReadyAt,
            HeartbeatAt = Heartbeat?.HeartbeatAt,
        };
}

/// <summary>
/// The heroesreplay processes that run one role's command from this install but are not in
/// <c>services.json</c> (#397): a child whose launcher gave up before it reported the pid, or one
/// a lost restart left behind. The supervisor takes one with a readable heartbeat over and stops
/// the others before it starts the role, so the role never runs twice and nothing runs
/// unsupervised. A process of another install is never counted.
/// </summary>
public static class UntrackedRoleProcesses
{
    /// <summary>
    /// The processes in <paramref name="processes"/> named heroesreplay, running
    /// <paramref name="installExecutable"/>, whose arguments are exactly
    /// <paramref name="role"/>'s command (<c>spectate heroesprofile</c>), and that are neither
    /// <paramref name="selfPid"/> nor in <paramref name="trackedPids"/>. A process whose path or
    /// command line cannot be read is not counted. Each carries the heartbeat
    /// <paramref name="heartbeatOrNull"/> finds for its pid.
    /// </summary>
    public static IReadOnlyList<UntrackedRoleProcess> Find(
        string role,
        IEnumerable<ProcessTableEntry> processes,
        Func<int, string> commandLineOrNull,
        string installExecutable,
        int selfPid,
        IReadOnlyCollection<int> trackedPids,
        Func<int, string, ServiceReadyReport> heartbeatOrNull = null
    )
    {
        ArgumentNullException.ThrowIfNull(commandLineOrNull);
        string command = ServiceProcessPlan.ArgumentsFor(role);
        if (command == null || string.IsNullOrWhiteSpace(installExecutable))
        {
            return Array.Empty<UntrackedRoleProcess>();
        }

        string[] expected = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tracked = new HashSet<int>(trackedPids ?? Array.Empty<int>());
        var found = new List<UntrackedRoleProcess>();
        foreach (ProcessTableEntry entry in processes ?? Array.Empty<ProcessTableEntry>())
        {
            if (
                entry == null
                || entry.Pid <= 0
                || entry.Pid == selfPid
                || tracked.Contains(entry.Pid)
                || !ServiceProcessPlan.IsHeroesReplay(entry.Name)
                || string.IsNullOrWhiteSpace(entry.ImagePath)
                || !ServiceProcessPlan.SamePath(entry.ImagePath, installExecutable)
            )
            {
                continue;
            }

            string[] arguments = ProcessCommandLine.Arguments(commandLineOrNull(entry.Pid));
            if (!arguments.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = ServiceProcessPlan.Names.First(item =>
                string.Equals(item, role, StringComparison.OrdinalIgnoreCase)
            );
            var process = new ServiceProcessRecord
            {
                Name = name,
                Pid = entry.Pid,
                Arguments = command,
                ExecutablePath = entry.ImagePath,
                StartedAt = entry.StartTime,
            };
            found.Add(new UntrackedRoleProcess(process, heartbeatOrNull?.Invoke(entry.Pid, name)));
        }

        return found;
    }
}
