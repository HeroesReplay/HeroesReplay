using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Providers;
using HeroesReplay.Core.Services.Twitch;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.Twitch.Commands;

public class ConnectCommand : Command
{
    public ConnectCommand()
        : base("connect", "Connect to the twitch channel for HeroesReplay.")
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
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        using IServiceScope scope = provider.CreateScope();
        using var waiter = new ManualResetEventSlim();
        IGameData gameData = scope.ServiceProvider.GetRequiredService<IGameData>();
        await gameData.LoadDataAsync();
        ITwitchBot twitchBot = scope.ServiceProvider.GetRequiredService<ITwitchBot>();
        await twitchBot.InitializeAsync();
        waiter.Wait(cancellationToken);
    }
}
