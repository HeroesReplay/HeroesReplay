using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// One role launch. <see cref="Record"/> is set once the process has a pid. <see cref="Cancelled"/>
/// means a stop request ended the ready wait. <see cref="Failure"/> is null when it got ready.
/// </summary>
public sealed record ServiceLaunch(
    ServiceProcessRecord Record,
    bool Ready,
    bool Cancelled,
    string Failure
);
