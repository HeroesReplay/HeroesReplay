using System;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>The steps <c>services reset</c> takes, injectable so a test drives them (#480).</summary>
public sealed class ServiceResetRequest
{
    public Func<ServiceSupervisorLiveness> SupervisorLiveness { get; init; }

    /// <summary>Queues the role for the supervisor (<see cref="ServiceResetFile.Request"/>).</summary>
    public Action<string> Request { get; init; }

    /// <summary>True while the supervisor has not taken the role's request yet.</summary>
    public Func<string, bool> Pending { get; init; }
    public Action<TimeSpan> Wait { get; init; }
    public TimeSpan Poll { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);
}
