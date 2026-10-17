using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using HeroesReplay.Core.ServiceHost;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// A supervisor whose .NET runtime was serviced under it hands over to a new one (#450). It
/// starts <c>services supervise --after-pid &lt;its pid&gt;</c> in a console of its own, the way
/// apply-release.ps1 attaches a supervisor, then exits. The new process loads the current runtime,
/// waits for the old one to exit, and claims the supervisor through the usual gate. The roles
/// keep running throughout.
/// </summary>
public static class ServiceSupervisorHandOver
{
    /// <summary>The hidden option of <c>services supervise</c> that names the old supervisor.</summary>
    public const string AfterPidOption = "--after-pid";

    /// <summary>How long the new supervisor waits for the old one to exit.</summary>
    public static readonly TimeSpan PredecessorWait = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The new supervisor: this install's exe, in a console window of its own
    /// (<see cref="ProcessStartInfo.UseShellExecute"/>), from the install folder.
    /// </summary>
    public static ProcessStartInfo Successor(string exe, int predecessorPid) =>
        new()
        {
            FileName = exe,
            Arguments = $"services supervise {AfterPidOption} {predecessorPid}",
            WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
            UseShellExecute = true,
        };

    /// <summary>
    /// Starts the new supervisor for this one. False, logged, when it did not start: this
    /// supervisor then keeps supervising on the serviced runtime.
    /// </summary>
    public static bool StartSuccessor(
        string exe,
        ILogger logger,
        Func<ProcessStartInfo, int?> start = null
    )
    {
        start ??= StartProcess;
        int self = Environment.ProcessId;
        ProcessStartInfo successor = Successor(exe, self);
        try
        {
            int? pid = start(successor);
            if (pid is not > 0)
            {
                throw new InvalidOperationException("No process was started.");
            }

            logger?.LogInformation(
                "Started the new supervisor, pid {Pid} (`{Exe} {Arguments}`), on the current .NET runtime [{Code}]. It takes over once this supervisor, pid {Self}, exits.",
                pid,
                successor.FileName,
                successor.Arguments,
                ServiceHealthCodes.RuntimeServiced,
                self
            );
            return true;
        }
        catch (Exception e)
        {
            logger?.LogError(
                e,
                "Could not start a new supervisor (`{Exe} {Arguments}`) [{Code}]. This supervisor keeps supervising on the serviced runtime. Run `heroesreplay services stop`, then `heroesreplay services start --supervise`.",
                successor.FileName,
                successor.Arguments,
                ServiceHealthCodes.RuntimeServiced
            );
            return false;
        }
    }

    /// <summary>
    /// The new supervisor's wait for the old one (<paramref name="pid"/>) to exit, up to
    /// <paramref name="limit"/>. True once it is gone.
    /// </summary>
    public static bool WaitForPredecessor(
        int pid,
        TimeSpan limit,
        Func<int, bool> alive = null,
        Action<TimeSpan> wait = null
    )
    {
        if (pid <= 0 || pid == Environment.ProcessId)
        {
            return true;
        }

        alive ??= IsAlive;
        wait ??= Thread.Sleep;
        int polls = Math.Max(1, (int)Math.Ceiling(limit / Poll));
        for (int poll = 0; poll < polls; poll++)
        {
            if (!alive(pid))
            {
                return true;
            }

            wait(Poll);
        }

        return !alive(pid);
    }

    private static int? StartProcess(ProcessStartInfo start)
    {
        using Process process = Process.Start(start);
        return process?.Id;
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
