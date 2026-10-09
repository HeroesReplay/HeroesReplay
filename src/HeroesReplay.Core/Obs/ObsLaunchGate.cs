using System;
using System.Threading;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// One OBS launch at a time across the processes of this logon session (#398): spectate's
/// <c>ObsCoordinator</c> and the supervisor's OBS watchdog both check for obs64 and start it
/// inside this gate, so they never start two (a second OBS stops on its "already running"
/// dialog).
/// </summary>
public sealed class ObsLaunchGate : IDisposable
{
    public const string MutexName = @"Local\HeroesReplay.ObsLaunch";
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(10);

    private readonly Mutex mutex;

    private ObsLaunchGate(Mutex mutex, bool held)
    {
        this.mutex = mutex;
        Held = held;
    }

    /// <summary>False when another launch held the gate for the whole wait.</summary>
    public bool Held { get; }

    public static ObsLaunchGate Enter(TimeSpan wait, string name = MutexName)
    {
        var mutex = new Mutex(false, name);
        bool held;
        try
        {
            held = mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            // The last holder died mid-launch. The gate is this caller's now.
            held = true;
        }

        return new ObsLaunchGate(mutex, held);
    }

    public void Dispose()
    {
        if (Held)
        {
            mutex.ReleaseMutex();
        }

        mutex.Dispose();
    }
}
