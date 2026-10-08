using System;
using System.IO;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// One supervisor across logon sessions (#293). <c>services start</c> and
/// <c>services supervise</c> ask here before they become the supervisor. A <c>Local\</c> mutex
/// exists per logon session, so an SSH session cannot see the desktop supervisor's mutex and
/// taking its own would start a second supervisor. The cross-session check
/// (<see cref="ServiceSupervisorFile.Check"/>: the mutex when this session sees it, otherwise
/// <c>supervisor.json</c>) therefore runs before the mutex is taken, and a refusal leaves
/// <c>supervisor.json</c> as it was.
/// </summary>
public sealed class ServiceSupervisorGate
{
    public string MutexName { get; init; } = ServiceSupervisorFile.MutexName;
    public string StatePath { get; init; } = ServiceSupervisorFile.DefaultPath;

    /// <summary>How recent <c>updatedAt</c> must be (<see cref="ServiceSupervisorFile.FreshFor"/>).</summary>
    public TimeSpan FreshFor { get; init; } = ServiceSupervisorFile.FreshFor(null);

    public TextWriter Error { get; init; } = Console.Error;

    /// <summary>
    /// <c>services start</c>. With <paramref name="supervise"/>, takes the claim this process then
    /// supervises under; without it, only checks that no supervisor runs. False, after writing
    /// why, when a supervisor already runs in this session or another. When it proceeds, no
    /// supervisor runs anywhere, so a <c>supervisor.json</c> left behind is a dead one's and is
    /// removed.
    /// </summary>
    public bool TryStart(bool supervise, out ServiceSupervisorMutex claim)
    {
        claim = supervise ? TryClaim() : null;
        if (supervise ? claim == null : !NoneRunning())
        {
            return false;
        }

        ServiceSupervisorFile.Delete(StatePath);
        return true;
    }

    /// <summary>
    /// <c>services supervise</c> and <c>services start --supervise</c>: the claim a new
    /// supervisor holds for its lifetime, or null after writing why.
    /// </summary>
    public ServiceSupervisorMutex TryClaim()
    {
        if (!NoneRunning())
        {
            return null;
        }

        ServiceSupervisorMutex claim = ServiceSupervisorMutex.TryAcquire(MutexName);
        if (claim == null)
        {
            // A supervisor in this session took the mutex after the check.
            Refuse(ServiceSupervisorLiveness.ByMutex);
        }

        return claim;
    }

    private bool NoneRunning()
    {
        ServiceSupervisorLiveness liveness = ServiceSupervisorFile.Check(
            MutexName,
            StatePath,
            FreshFor
        );
        if (!liveness.Running)
        {
            return true;
        }

        Refuse(liveness);
        return false;
    }

    /// <summary>
    /// <c>supervisor already running: pid 14420, seen via supervisor.json</c> (another session)
    /// or <c>…, seen via its mutex</c> (this one), then what to do.
    /// </summary>
    private void Refuse(ServiceSupervisorLiveness liveness)
    {
        int? pid = ServiceSupervisorFile.TryLoad(StatePath)?.Pid;
        string via =
            liveness.SeenVia == ServiceSupervisorLiveness.ViaStateFile
                ? "seen via supervisor.json"
                : "seen via its mutex";
        Error.WriteLine($"supervisor already running: {(pid > 0 ? $"pid {pid}, " : "")}{via}");
        Error.WriteLine(
            "Run `heroesreplay services stop` first; it stops the roles and the supervisor."
        );
    }
}
