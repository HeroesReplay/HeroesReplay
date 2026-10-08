using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.ServiceHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands.Spectate.Commands;

public class SpectateHeroesProfileApiCommand : Command
{
    public SpectateHeroesProfileApiCommand()
        : base(
            "heroesprofile",
            "Spectate StormReplay files already in Data\\Standard and Data\\Requests. Does not call Heroes Profile."
        )
    {
        SetAction(
            async (parseResult, cancellationToken) =>
            {
                return await CommandAsync(cancellationToken);
            }
        );
    }

    protected async Task<int> CommandAsync(CancellationToken cancellationToken)
    {
        AspireDashboardHost.EnsureRunning();
        using ServiceStopLink stop = ServiceStopFile.Link(cancellationToken);
        using ServiceProvider provider = new ServiceCollection()
            .AddSpectateServices(stop.Token, typeof(ReplayCacheProvider))
            .BuildHeroesReplayProvider();
        using IServiceScope scope = provider.CreateScope();
        AppSettings settings = scope.ServiceProvider.GetRequiredService<AppSettings>();
        // The OBS websocket, read-only, while OBS runs and ServiceHealth:SpectateObsProbe is on
        // (#305). It runs beside the startup steps below.
        var probes = new ServiceDependencyMonitor(
            new ObsWebsocketProbe(settings.OBS, settings.ServiceHealth?.SpectateObsProbe == true),
            settings.ServiceHealth,
            scope.ServiceProvider.GetRequiredService<ILogger<ServiceDependencyMonitor>>()
        );
        Task<ServiceDependencyResult> firstProbe = probes.FirstAsync(stop.Token);
        SpectateReleaseVersion.Write(scope.ServiceProvider);
        SpectateClipTools.Check(scope.ServiceProvider);
        scope.ServiceProvider.GetRequiredService<BattleNetAgentReaper>().Reap("spectate start");
        IEngine engine = scope.ServiceProvider.GetRequiredService<IEngine>();
        using ServiceHeartbeat heartbeat = ServiceHeartbeat.StartFromEnvironment(
            "spectate",
            settings.ServiceHealth,
            stop.Token,
            await firstProbe
        );
        using IDisposable probing = probes.Watch(stop.Token);
        return await engine.RunAsync() ? 0 : 1;
    }
}
