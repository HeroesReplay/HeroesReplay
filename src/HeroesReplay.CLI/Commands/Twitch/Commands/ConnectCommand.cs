using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Processes;
using HeroesReplay.Core.Services.Twitch;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.Twitch.Commands;

public class ConnectCommand : Command
{
    public ConnectCommand()
        : base(
            "connect",
            "Connect chat and watch spectator status for Blue/Red predictions. Does not launch the game."
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
            .AddTwitchServices(stop.Token)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        using IServiceScope scope = provider.CreateScope();
        IGameData gameData = scope.ServiceProvider.GetRequiredService<IGameData>();
        await gameData.LoadDataAsync();
        ITwitchBot twitchBot = scope.ServiceProvider.GetRequiredService<ITwitchBot>();
        await twitchBot.InitializeAsync();
        StatusPredictionWatcher predictions =
            scope.ServiceProvider.GetRequiredService<StatusPredictionWatcher>();
        await predictions.WatchAsync(stop.Token);
    }
}
