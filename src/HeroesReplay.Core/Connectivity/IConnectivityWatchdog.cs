using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Connectivity;

public interface IConnectivityWatchdog
{
    bool IsOnline { get; }

    /// <summary>How long the internet probe has been failing. Online is zero.</summary>
    TimeSpan DownFor => TimeSpan.Zero;

    ConnectivitySnapshot Last { get; }
    event EventHandler<ConnectivityChangedEventArgs> Changed;
    Task<ConnectivitySnapshot> ProbeAsync(CancellationToken cancellationToken);
    bool Apply(ConnectivitySnapshot snapshot);
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// True when the coming shutdown is a release restart: a live stream stays up on the waiting
    /// scene for the new install instead of being stopped.
    /// </summary>
    void KeepStreamThroughRestart(bool keep) { }
}
