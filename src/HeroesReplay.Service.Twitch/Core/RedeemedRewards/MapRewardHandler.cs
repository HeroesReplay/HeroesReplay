using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Queue;
using HeroesReplay.Service.Twitch.Core.Rewards;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwitchLib.Client.Interfaces;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Service.Twitch.Core.RedeemedRewards
{
    public class MapRewardHandler : IRewardHandler
    {
        private readonly ILogger<MapRewardHandler> logger;
        private readonly IRewardRequestFactory rewardRequestFactory;
        private readonly ITwitchClient twitchClient;
        private readonly IRequestQueue queue;
        private readonly TwitchOptions twitchOptions;

        public MapRewardHandler(
            ILogger<MapRewardHandler> logger, 
            IRewardRequestFactory rewardRequestFactory, 
            ITwitchClient twitchClient,
            IRequestQueue queue,
            IOptions<TwitchOptions> twitchOptions)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.rewardRequestFactory = rewardRequestFactory ?? throw new ArgumentNullException(nameof(rewardRequestFactory));
            this.twitchClient = twitchClient ?? throw new ArgumentNullException(nameof(twitchClient));
            this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
            this.twitchOptions = twitchOptions.Value;
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

                    if (twitchOptions.EnableChatBot)
                    {
                        twitchClient.SendMessage(twitchOptions.Channel, $"{args.DisplayName}, {response.Message}", twitchOptions.DryRunMode);
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
