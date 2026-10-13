using System;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// One role launch. <see cref="Record"/> is set once the process has a pid. <see cref="Cancelled"/>
/// means a stop request ended the ready wait. <see cref="Failure"/> is null when it got ready.
/// <see cref="StillRunning"/> means it did not get ready and could not be stopped again: the
/// process in <see cref="Record"/> is still alive, so the caller must keep tracking it (#397).
/// </summary>
public sealed record ServiceLaunch(
    ServiceProcessRecord Record,
    bool Ready,
    bool Cancelled,
    string Failure,
    bool StillRunning = false
);

/// <summary>
/// What the supervisor asks of one restart (#397): the role, how long to wait for its ready file
/// (longer while the machine is short of memory), the callback that records the process as soon
/// as it has a pid, and one that runs on each pause of the ready wait so <c>supervisor.json</c>
/// stays fresh through a long wait.
/// </summary>
public sealed record ServiceLaunchRequest(
    string Role,
    TimeSpan ReadyTimeout,
    Action<ServiceProcessRecord> Started,
    Action Waiting = null
);
