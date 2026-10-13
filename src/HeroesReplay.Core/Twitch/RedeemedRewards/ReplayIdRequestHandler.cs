using System.Collections.Generic;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Status;
using HeroesReplay.Core.Twitch.Rewards;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Interfaces;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Twitch.RedeemedRewards;

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
        if (PlayerPriorityRequest.TryRead(args.Message, out int replayId, out string battleTag))
        {
            Task.Factory.StartNew(
                async () =>
                {
                    SpectatorStatus status = statusStore.Read();
                    if (
                        battleTag != null
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
                    if (queued != null && !response.Duplicate)
                    {
                        logger.LogInformation(
                            "Redemption {RedemptionId} was not queued ({Status}). Its cancel is recorded, and twitch connect returns the points.",
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
            // Nothing can be queued, so the points go back (#425): a cancel is recorded, and
            // twitch connect sends it (RedemptionFulfiller).
            bool refund = args.RedemptionId != System.Guid.Empty;
            if (refund)
            {
                RedemptionDispositionLog.Append(
                    System.IO.Path.Combine(
                        settings.Location.DataDirectory,
                        RedemptionDispositionLog.FileName
                    ),
                    null,
                    requestFactory.Create(reward, args),
                    RedemptionEnd.Cancel
                );
            }

            logger.LogInformation(
                "Redemption {RedemptionId} was not queued ({Status}). {Refund}",
                args.RedemptionId,
                RewardRedemptionStatus.Decide(RewardTerminal.InvalidInput),
                refund
                    ? "Its cancel is recorded, and twitch connect returns the points."
                    : "It has no redemption to refund."
            );
            twitchClient.SendMessage(
                settings.Twitch.Channel,
                refund
                    ? $"{args.DisplayName}, your request is invalid. Your points will be returned. Enter a ReplayId such as 12345678, or ReplayId,BattleTag."
                    : $"{args.DisplayName}, your request is invalid.",
                dryRun: settings.Twitch.DryRunMode
            );
            logger.LogDebug($"{args.TimeStamp}: {args.RewardId} - {args.RewardCost}");
        }
    }
}
