using System;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Processes;
using HeroesReplay.Core.Services.Twitch;
using HeroesReplay.Core.Services.Twitch.Rewards;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands.Twitch.Commands;

public class ConnectCommand : Command
{
    public ConnectCommand()
        : base(
            "connect",
            "Connect chat, sync channel-point rewards, and watch spectator status for Blue/Red predictions. Does not launch the game."
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
            .BuildHeroesReplayProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        using Activity ready = HeroesReplayTelemetry.StartSpan("heroesreplay.service.ready");
        using IServiceScope scope = provider.CreateScope();
        IGameData gameData = scope.ServiceProvider.GetRequiredService<IGameData>();
        await gameData.LoadDataAsync();
        ILogger<ConnectCommand> logger = scope.ServiceProvider.GetRequiredService<
            ILogger<ConnectCommand>
        >();
        try
        {
            ITwitchRewardsManager rewards =
                scope.ServiceProvider.GetRequiredService<ITwitchRewardsManager>();
            await rewards.CreateOrUpdateAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Twitch channel rewards were not synced. Chat will still connect."
            );
        }

        ITwitchBot twitchBot = scope.ServiceProvider.GetRequiredService<ITwitchBot>();
        await twitchBot.InitializeAsync();
        ServiceReadyFile.ReportFromEnvironment("twitch");
        StatusPredictionWatcher predictions =
            scope.ServiceProvider.GetRequiredService<StatusPredictionWatcher>();
        await predictions.WatchAsync(stop.Token);
    }
}
