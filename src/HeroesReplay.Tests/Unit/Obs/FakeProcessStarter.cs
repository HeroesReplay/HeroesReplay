using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using HeroesReplay.Core.Obs;

namespace HeroesReplay.Tests.Unit.Obs;

/// <summary>
/// Records each start and starts nothing (#409). <see cref="OnRun"/> is what the fake
/// <c>cmd /c start</c> did: by default it ran as pid <see cref="CmdPid"/> and exited 0.
/// </summary>
internal sealed class FakeProcessStarter : IProcessStarter
{
    public const int CmdPid = 6000;

    public List<ProcessStartInfo> Starts { get; } = new();
    public Func<ProcessStartInfo, ProcessRun> OnRun { get; set; }

    public ProcessRun Run(ProcessStartInfo start, TimeSpan wait)
    {
        Starts.Add(start);
        return OnRun?.Invoke(start) ?? new ProcessRun(CmdPid, true, 0);
    }
}

/// <summary>Reads a test's own launch gate (#409) from another thread, as another process would.</summary>
internal static class LaunchGateProbe
{
    public static string NewName() =>
        @"Local\HeroesReplay.Tests.ObsLaunch." + Guid.NewGuid().ToString("N");

    /// <summary>
    /// True when someone else holds the gate: it cannot be entered without a wait. A thread of
    /// its own, never an inlined task: a mutex lets the thread that holds it in again.
    /// </summary>
    public static bool HeldElsewhere(string name)
    {
        bool held = false;
        var probe = new Thread(() =>
        {
            using ObsLaunchGate other = ObsLaunchGate.Enter(TimeSpan.Zero, name);
            held = !other.Held;
        });
        probe.Start();
        probe.Join();
        return held;
    }
}
