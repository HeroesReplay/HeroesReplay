using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>How one recorded role ended during <c>services stop</c>.</summary>
public enum ServiceStopOutcome
{
    /// <summary>The process was not running when the stop began.</summary>
    AlreadyExited,

    /// <summary>The process exited after the stop request, without a kill.</summary>
    Graceful,

    /// <summary>The process was killed after the graceful budget and is gone.</summary>
    Killed,

    /// <summary>The process is still running after the kill. The stop failed.</summary>
    StillRunning,
}

public sealed record ServiceRoleStop(
    string Name,
    int Pid,
    ServiceStopOutcome Outcome,
    string Detail = null
)
{
    public bool Exited => Outcome != ServiceStopOutcome.StillRunning;

    public string Describe()
    {
        string outcome = Outcome switch
        {
            ServiceStopOutcome.AlreadyExited => "already exited",
            ServiceStopOutcome.Graceful => "graceful",
            ServiceStopOutcome.Killed => "killed",
            _ => "still running",
        };
        return string.IsNullOrWhiteSpace(Detail)
            ? $"{Name} pid {Pid}: {outcome}."
            : $"{Name} pid {Pid}: {outcome}. {Detail}";
    }
}

public enum ServiceStreamState
{
    NotRunning,
    Unreachable,
    Inactive,
    Active,
    Unknown,
}

/// <summary>
/// Read-only OBS stream state after every role exited. OBS that is closed or has no reachable
/// websocket is not streaming for this check. An active stream, or OBS that did not report its
/// stream state, fails the stop.
/// </summary>
public sealed record ServiceStreamCheck(ServiceStreamState State, string Detail)
{
    public bool ConfirmsStopped =>
        State
            is ServiceStreamState.NotRunning
                or ServiceStreamState.Unreachable
                or ServiceStreamState.Inactive;

    public static ServiceStreamCheck NotRunning() =>
        new(ServiceStreamState.NotRunning, "OBS is not running.");

    public static ServiceStreamCheck Unreachable(string detail) =>
        new(ServiceStreamState.Unreachable, detail);

    public static ServiceStreamCheck Inactive() =>
        new(ServiceStreamState.Inactive, "OBS reported the stream inactive.");

    public static ServiceStreamCheck Active() =>
        new(ServiceStreamState.Active, "OBS reported the stream active.");

    public static ServiceStreamCheck Unknown(string detail) =>
        new(ServiceStreamState.Unknown, detail);

    public string Describe()
    {
        string state = State switch
        {
            ServiceStreamState.NotRunning => "not running",
            ServiceStreamState.Unreachable => "websocket unreachable, treated as not streaming",
            ServiceStreamState.Inactive => "not streaming",
            ServiceStreamState.Active => "STREAMING",
            _ => "not confirmed",
        };
        return string.IsNullOrWhiteSpace(Detail) ? state + "." : $"{state}. {Detail}";
    }
}

/// <summary>
/// The outcome of <c>services stop</c>. It succeeds only when every recorded role exited, the game
/// closed (when spectate was recorded), and OBS is confirmed not streaming.
/// </summary>
public sealed class ServiceStopResult
{
    public IReadOnlyList<ServiceRoleStop> Roles { get; init; } = Array.Empty<ServiceRoleStop>();

    /// <summary>Null when the game was not checked: spectate was not recorded.</summary>
    public bool? GameClosed { get; init; }

    /// <summary>Null when OBS was not read: a role is still running, or no reader was given.</summary>
    public ServiceStreamCheck Stream { get; init; }

    public bool RolesExited => Roles.All(role => role.Exited);

    public bool Succeeded =>
        RolesExited && GameClosed != false && (Stream == null || Stream.ConfirmsStopped);

    public int ExitCode => Succeeded ? 0 : 1;

    public IReadOnlyList<string> Failures()
    {
        var failures = new List<string>();
        foreach (ServiceRoleStop role in Roles.Where(role => !role.Exited))
        {
            failures.Add($"{role.Name} pid {role.Pid} is still running.");
        }

        if (GameClosed == false)
        {
            failures.Add("Heroes of the Storm is still running.");
        }

        if (Stream != null && !Stream.ConfirmsStopped)
        {
            failures.Add(
                Stream.State == ServiceStreamState.Active
                    ? "OBS is still streaming. Stop the stream in OBS."
                    : "OBS stream state was not confirmed. " + Stream.Detail
            );
        }

        return failures;
    }
}
