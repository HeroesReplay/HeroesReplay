using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.Core;
using HeroesReplay.Core.Services.Processes;
using HeroesReplay.Core.Services.Providers;
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
                await CommandAsync(cancellationToken);
            }
        );
    }

    protected async Task CommandAsync(CancellationToken cancellationToken)
    {
        using ServiceStopLink stop = ServiceStopFile.Link(cancellationToken);
        using ServiceProvider provider = new ServiceCollection()
            .AddSpectateServices(stop.Token, typeof(ReplayCacheProvider))
            .BuildHeroesReplayProvider();
        using IServiceScope scope = provider.CreateScope();
        IEngine engine = scope.ServiceProvider.GetRequiredService<IEngine>();
        await engine.RunAsync();
    }
}
