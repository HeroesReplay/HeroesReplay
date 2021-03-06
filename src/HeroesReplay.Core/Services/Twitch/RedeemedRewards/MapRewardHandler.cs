﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Twitch.Rewards;
using Microsoft.Extensions.Logging;
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
        private readonly AppSettings settings;

        public MapRewardHandler(ILogger<MapRewardHandler> logger, IRewardRequestFactory rewardRequestFactory, ITwitchClient twitchClient, IRequestQueue queue, AppSettings settings)
        {
            this.logger = logger;
            this.rewardRequestFactory = rewardRequestFactory;
            this.twitchClient = twitchClient;
            this.queue = queue;
            this.settings = settings;
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

                    if (settings.Twitch.EnableChatBot)
                    {
                        twitchClient.SendMessage(settings.Twitch.Channel, $"{args.DisplayName}, {response.Message}", dryRun: settings.Twitch.DryRunMode);
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
