using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.ServiceHost;
using Microsoft.Extensions.DependencyInjection;

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
        SpectateReleaseVersion.Write(scope.ServiceProvider);
        IEngine engine = scope.ServiceProvider.GetRequiredService<IEngine>();
        using ServiceHeartbeat heartbeat = ServiceHeartbeat.StartFromEnvironment(
            "spectate",
            scope.ServiceProvider.GetRequiredService<AppSettings>().ServiceHealth,
            stop.Token
        );
        return await engine.RunAsync() ? 0 : 1;
    }
}
