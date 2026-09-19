using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.Services.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.Spectate.Commands;

public class SpectateHeroesProfileApiCommand : Command
{
    public SpectateHeroesProfileApiCommand()
        : base(
            "heroesprofile",
            "Access the HeroesProfile S3 bucket to download uploaded replays and spectate them."
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
        using ServiceProvider provider = new ServiceCollection()
            .AddSpectateServices(cancellationToken, typeof(HeroesProfileProvider))
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        IEngine engine = scope.ServiceProvider.GetRequiredService<IEngine>();
        await engine.RunAsync();
    }
}
