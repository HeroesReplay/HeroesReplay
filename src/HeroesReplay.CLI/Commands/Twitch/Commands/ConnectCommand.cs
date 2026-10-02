using System;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.Twitch.Predictions;
using HeroesReplay.Core.Twitch.Rewards;
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
        AppSettings settings = scope.ServiceProvider.GetRequiredService<AppSettings>();
        if (!ShouldSyncRewards(settings.Twitch))
        {
            logger.LogInformation(
                "Channel-point rewards were not synced: Twitch:EnablePubSub and Twitch:EnableRequests are both off."
            );
        }
        else
        {
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
        }

        ITwitchBot twitchBot = scope.ServiceProvider.GetRequiredService<ITwitchBot>();
        await twitchBot.InitializeAsync();
        ServiceReadyFile.ReportFromEnvironment("twitch");
        ServiceReadyFile.ReportHeartbeatFromEnvironment();
        StatusPredictionWatcher predictions =
            scope.ServiceProvider.GetRequiredService<StatusPredictionWatcher>();
        await predictions.WatchAsync(stop.Token);
    }

    /// <summary>Rewards are only needed when redemptions are handled. Syncing edits the live channel (#146).</summary>
    public static bool ShouldSyncRewards(TwitchSettings twitch) =>
        twitch is not null && (twitch.EnablePubSub || twitch.EnableRequests);
}
