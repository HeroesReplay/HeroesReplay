﻿using HeroesReplay.Core.Configuration;

using Microsoft.Extensions.Logging;

using System.Collections.Generic;
using System.Threading.Tasks;

using TwitchLib.Client.Interfaces;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.RewardHandlers
{
    public class ReplayIdRequestHandler : IRewardHandler
    {
        private readonly ILogger<ReplayIdRequestHandler> logger;
        private readonly IRewardRequestFactory requestFactory;
        private readonly ITwitchClient twitchClient;
        private readonly IReplayRequestQueue queue;
        private readonly AppSettings settings;

        public ReplayIdRequestHandler(ILogger<ReplayIdRequestHandler> logger, IRewardRequestFactory requestFactory, ITwitchClient twitchClient, IReplayRequestQueue queue, AppSettings settings)
        {
            this.logger = logger;
            this.requestFactory = requestFactory;
            this.twitchClient = twitchClient;
            this.queue = queue;
            this.settings = settings;
        }

        public IEnumerable<RewardType> Supports => new[] { RewardType.ReplayId };

        public void Execute(SupportedReward reward, OnRewardRedeemedArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.Message) && int.TryParse(args.Message.Trim(), out int replayId))
            {
                Task.Factory.StartNew(async () =>
                {
                    RewardResponse response = await queue.EnqueueRequestAsync(requestFactory.Create(reward, args));

                    if (settings.Twitch.EnableChatBot)
                    {
                        string message = $"{args.DisplayName}, {response.Message}";
                        twitchClient.SendMessage(settings.Twitch.Channel, message, dryRun: settings.Twitch.DryRunMode);
                    }

                }, TaskCreationOptions.LongRunning);
            }
            else
            {
                twitchClient.SendMessage(settings.Twitch.Channel, $"{args.DisplayName}, your request is invalid.", dryRun: settings.Twitch.DryRunMode);
                logger.LogDebug($"{args.TimeStamp}: {args.RewardId} - {args.RewardCost}");
            }
        }
    }
}
