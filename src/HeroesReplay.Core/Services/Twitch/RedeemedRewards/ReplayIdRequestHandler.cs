using System.Collections.Generic;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Status;
using HeroesReplay.Core.Services.Twitch.Rewards;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Interfaces;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.RedeemedRewards;

public class ReplayIdRequestHandler : IRewardHandler
{
    private readonly ILogger<ReplayIdRequestHandler> logger;
    private readonly IRewardRequestFactory requestFactory;
    private readonly ITwitchClient twitchClient;
    private readonly IRequestQueue queue;
    private readonly AppSettings settings;
    private readonly SpectatorStatusStore statusStore;
    private readonly IRedemptionCanceller redemptions;

    public ReplayIdRequestHandler(
        ILogger<ReplayIdRequestHandler> logger,
        IRewardRequestFactory requestFactory,
        ITwitchClient twitchClient,
        IRequestQueue queue,
        AppSettings settings,
        SpectatorStatusStore statusStore,
        IRedemptionCanceller redemptions
    )
    {
        this.logger = logger;
        this.requestFactory = requestFactory;
        this.twitchClient = twitchClient;
        this.queue = queue;
        this.settings = settings;
        this.statusStore = statusStore;
        this.redemptions = redemptions;
    }

    public IEnumerable<RewardType> Supports => new[] { RewardType.ReplayId };

    public void Execute(SupportedReward reward, OnRewardRedeemedArgs args)
    {
        if (PlayerPriorityRequest.TryRead(args.Message, out int replayId, out int? playerIndex))
        {
            Task.Factory.StartNew(
                async () =>
                {
                    SpectatorStatus status = statusStore.Read();
                    if (
                        playerIndex.HasValue
                        && PlayerPriorityRequest.BlocksBecauseMatchStarted(
                            status?.ReplayId,
                            status?.Phase,
                            replayId
                        )
                    )
                    {
                        bool returned = await redemptions
                            .TryCancelAsync(args.ChannelId, args.RewardId, args.RedemptionId)
                            .ConfigureAwait(false);
                        string message = returned
                            ? $"{args.DisplayName}, that match has already started. The channel points were returned."
                            : $"{args.DisplayName}, that match has already started, so this player focus was not queued.";
                        twitchClient.SendMessage(
                            settings.Twitch.Channel,
                            message,
                            dryRun: settings.Twitch.DryRunMode
                        );
                        return;
                    }

                    RewardResponse response = await queue.EnqueueItemAsync(
                        requestFactory.Create(reward, args)
                    );
                    string queued = RewardRedemptionStatus.ForQueue(response);
                    if (queued != null)
                    {
                        logger.LogInformation(
                            "Redemption {RedemptionId} is {Status}. Twitch was not called.",
                            args.RedemptionId,
                            queued
                        );
                    }

                    if (response.Duplicate)
                    {
                        logger.LogInformation(
                            "{Login} redeemed '{Title}' again. Not queueing it twice.",
                            args.Login,
                            reward.Title
                        );
                        return;
                    }

                    if (settings.Twitch.EnableChatBot)
                    {
                        string message = $"{args.DisplayName}, {response.Message}";
                        twitchClient.SendMessage(
                            settings.Twitch.Channel,
                            message,
                            dryRun: settings.Twitch.DryRunMode
                        );
                    }
                },
                TaskCreationOptions.LongRunning
            );
        }
        else
        {
            logger.LogInformation(
                "Redemption {RedemptionId} is {Status}. Twitch was not called.",
                args.RedemptionId,
                RewardRedemptionStatus.Decide(RewardTerminal.InvalidInput)
            );
            twitchClient.SendMessage(
                settings.Twitch.Channel,
                $"{args.DisplayName}, your request is invalid.",
                dryRun: settings.Twitch.DryRunMode
            );
            logger.LogDebug($"{args.TimeStamp}: {args.RewardId} - {args.RewardCost}");
        }
    }
}
