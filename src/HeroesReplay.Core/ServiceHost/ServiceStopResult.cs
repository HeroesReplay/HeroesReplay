using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Core.Shared;

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

    /// <summary>OBS is running without an answering websocket, and this install does not stream.</summary>
    NotStreamedHere,
}

/// <summary>
/// Read-only OBS stream state after every role exited. Only a closed OBS or one that reports the
/// stream inactive confirms the stop. An active stream, an OBS that is running but whose websocket
/// does not answer (it may still be live), or one that did not report its stream state fails it.
/// </summary>
public sealed record ServiceStreamCheck(ServiceStreamState State, string Detail)
{
    public bool ConfirmsStopped =>
        State
            is ServiceStreamState.NotRunning
                or ServiceStreamState.Inactive
                or ServiceStreamState.NotStreamedHere;

    public static ServiceStreamCheck NotRunning() =>
        new(ServiceStreamState.NotRunning, "OBS is not running.");

    public static ServiceStreamCheck Unreachable(string detail) =>
        new(ServiceStreamState.Unreachable, detail);

    /// <summary>
    /// OBS is running but its websocket did not answer. That holds the stop only when this install
    /// streams (<c>OBS:StreamingEnabled</c>): otherwise HeroesReplay never started a stream there.
    /// </summary>
    public static ServiceStreamCheck WhenUnreachable(bool streamedHere, string detail) =>
        streamedHere
            ? Unreachable(detail)
            : new(
                ServiceStreamState.NotStreamedHere,
                detail + " OBS:StreamingEnabled is false, so this install did not stream."
            );

    public static ServiceStreamCheck Inactive() =>
        new(ServiceStreamState.Inactive, "OBS reported the stream inactive.");

    public static ServiceStreamCheck Active() =>
        new(ServiceStreamState.Active, "OBS reported the stream active.");

    public static ServiceStreamCheck Unknown(string detail) =>
        new(ServiceStreamState.Unknown, detail);

    /// <summary>
    /// From the stream's health (#395). An output that is active but reconnecting or frozen is
    /// still up: OBS may put it back on air, so it does not confirm the stop either.
    /// </summary>
    public static ServiceStreamCheck From(ObsStreamHealth health) =>
        health?.State switch
        {
            ObsStreamState.Inactive => Inactive(),
            ObsStreamState.Live => Active(),
            ObsStreamState.Reconnecting or ObsStreamState.Stalled => new(
                ServiceStreamState.Active,
                "OBS reported the stream output still active, "
                    + health.State.ToString().ToLowerInvariant()
                    + "."
            ),
            _ => Unknown(health?.Detail ?? "GetStreamStatus returned no status."),
        };

    public string Describe()
    {
        string state = State switch
        {
            ServiceStreamState.NotRunning => "not running",
            ServiceStreamState.Unreachable =>
                "running, but its websocket did not answer, so the stream may still be live",
            ServiceStreamState.Inactive => "not streaming",
            ServiceStreamState.Active => "STREAMING",
            ServiceStreamState.NotStreamedHere => "running, not checked",
            _ => "not confirmed",
        };
        return string.IsNullOrWhiteSpace(Detail) ? state + "." : $"{state}. {Detail}";
    }
}

/// <summary>
/// The outcome of <c>services stop</c>. It succeeds only when every recorded role (and a
/// hand-started spectate from this install, #381) exited, the game closed when the stop owns it,
/// OBS is confirmed not streaming, and no recording spectate started is left running.
/// </summary>
public sealed class ServiceStopResult
{
    public IReadOnlyList<ServiceRoleStop> Roles { get; init; } = Array.Empty<ServiceRoleStop>();

    /// <summary>
    /// Null when the game was not checked: roles were recorded without spectate, or a spectate
    /// from another install runs it (#381).
    /// </summary>
    public bool? GameClosed { get; init; }

    /// <summary>
    /// The HeroesSwitcher_x64 processes closed after the game (#359). Null when they were not
    /// checked (as for <see cref="GameClosed"/>), or no step was given.
    /// </summary>
    public SwitcherStopResult Switchers { get; init; }

    /// <summary>Null when OBS was not read: a role is still running, or no reader was given.</summary>
    public ServiceStreamCheck Stream { get; init; }

    /// <summary>
    /// The recording spectate started and left running (#318). Null when it was not checked: a
    /// role is still running, or no step was given.
    /// </summary>
    public OrphanRecordingCheck Recording { get; init; }

    /// <summary>Null when no supervisor was running.</summary>
    public ServiceRoleStop Supervisor { get; init; }

    /// <summary>The last line the stop printed, for example "Nothing to stop." (#381).</summary>
    public string Summary { get; set; }

    public bool RolesExited => Roles.All(role => role.Exited);

    public bool Succeeded =>
        RolesExited
        && GameClosed != false
        && Switchers?.AllStopped != false
        && (Stream == null || Stream.ConfirmsStopped)
        && (Recording == null || Recording.ConfirmsStopped)
        && Supervisor?.Exited != false;

    public int ExitCode => Succeeded ? 0 : 1;

    /// <summary>
    /// The <c>Heroes of the Storm:</c> line: the game, then each HeroesSwitcher_x64 closed after
    /// it or left because its Heroes child is still up (#359).
    /// </summary>
    public static string DescribeGame(bool closed, SwitcherStopResult switchers)
    {
        string game = closed
            ? "Heroes of the Storm: closed."
            : "Heroes of the Storm: still running.";
        string more = switchers?.Describe();
        return string.IsNullOrEmpty(more) ? game : game + " " + more;
    }

    /// <summary>
    /// The <c>Heroes of the Storm:</c> line when no game was running to close: no spectate was
    /// recorded or found (#381). Each idle switcher closed is still named.
    /// </summary>
    public static string DescribeGameNotRunning(SwitcherStopResult switchers)
    {
        const string game = "Heroes of the Storm: not running.";
        string more = switchers?.Describe();
        return string.IsNullOrEmpty(more) ? game : game + " " + more;
    }

    public IReadOnlyList<string> Failures()
    {
        var failures = new List<string>();
        foreach (ServiceRoleStop role in Roles.Where(role => !role.Exited))
        {
            failures.Add($"{role.Name} pid {role.Pid} is still running.");
        }

        if (Supervisor?.Exited == false)
        {
            failures.Add(
                $"The supervisor pid {Supervisor.Pid} is still running. The stop file stays down so it restarts nothing."
            );
        }

        if (GameClosed == false)
        {
            failures.Add("Heroes of the Storm is still running.");
        }

        foreach (
            SwitcherStop switcher in Switchers?.Stopped.Where(stop =>
                stop.Outcome == SwitcherStopOutcome.StillRunning
            ) ?? []
        )
        {
            failures.Add($"{NamedProcess.HeroesSwitcher} pid {switcher.Pid} is still running.");
        }

        if (Stream != null && !Stream.ConfirmsStopped)
        {
            failures.Add(
                Stream.State == ServiceStreamState.Active
                    ? "OBS is still streaming. Stop the stream in OBS."
                : Stream.State == ServiceStreamState.Unreachable
                    ? "OBS is running, but its websocket did not answer, so the stream was not confirmed stopped. "
                        + Stream.Detail
                        + " Stop the stream in OBS, or enable Tools > WebSocket Server Settings and run `heroesreplay services stop` again."
                : "OBS stream state was not confirmed. " + Stream.Detail
            );
        }

        if (Recording != null && !Recording.ConfirmsStopped)
        {
            failures.Add(
                "The OBS recording spectate started was not confirmed stopped. "
                    + Recording.Detail
                    + " Then run `heroesreplay services stop` again."
            );
        }

        return failures;
    }
}
