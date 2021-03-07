using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Twitch.Rewards;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using TwitchLib.Client.Interfaces;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.RedeemedRewards
{
    public class MapRewardHandler : IRewardHandler
    {
        private readonly ILogger<MapRewardHandler> logger;
        private readonly IRewardRequestFactory rewardRequestFactory;
        private readonly ITwitchClient twitchClient;
        private readonly IRequestQueue queue;
        private readonly IOptions<AppSettings> settings;

        public MapRewardHandler(ILogger<MapRewardHandler> logger, IOptions<AppSettings> settings, IRewardRequestFactory rewardRequestFactory, ITwitchClient twitchClient, IRequestQueue queue)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.rewardRequestFactory = rewardRequestFactory ?? throw new ArgumentNullException(nameof(rewardRequestFactory));
            this.twitchClient = twitchClient ?? throw new ArgumentNullException(nameof(twitchClient));
            this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        private readonly RewardType[] Supported = new[]
        {
            RewardType.ARAM,
            RewardType.QM,
            RewardType.UD,
            RewardType.SL,

            RewardType.QM | RewardType.Map,
            RewardType.ARAM | RewardType.Map,
            RewardType.UD | RewardType.Map,
            RewardType.SL | RewardType.Map,

            RewardType.QM | RewardType.Map | RewardType.Rank,
            RewardType.ARAM | RewardType.Map | RewardType.Rank,
            RewardType.UD | RewardType.Map | RewardType.Rank,
            RewardType.SL | RewardType.Map | RewardType.Rank,
        };

        public IEnumerable<RewardType> Supports => Supported;

        public void Execute(SupportedReward reward, OnRewardRedeemedArgs args)
        {
            Task.Factory.StartNew(async () =>
            {
                try
                {
                    logger.LogInformation($"{args.Login} has redeemed {args.RewardTitle}");

                    RewardResponse response = await queue.EnqueueItemAsync(rewardRequestFactory.Create(reward, args));

                    if (settings.Value.Twitch.EnableChatBot)
                    {
                        twitchClient.SendMessage(settings.Value.Twitch.Channel, $"{args.DisplayName}, {response.Message}", dryRun: settings.Value.Twitch.DryRunMode);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Could not queue reward: {reward.Title}");
                }

            }, TaskCreationOptions.LongRunning);
        }
    }
}
