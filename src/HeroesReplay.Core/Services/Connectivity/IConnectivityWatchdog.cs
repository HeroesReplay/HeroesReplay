using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Connectivity;

public interface IConnectivityWatchdog
{
    bool IsOnline { get; }
    ConnectivitySnapshot Last { get; }
    event EventHandler<ConnectivityChangedEventArgs> Changed;
    Task<ConnectivitySnapshot> ProbeAsync(CancellationToken cancellationToken);
    bool Apply(ConnectivitySnapshot snapshot);
    Task RunAsync(CancellationToken cancellationToken);
}
