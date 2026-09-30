using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Connectivity;

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
}
