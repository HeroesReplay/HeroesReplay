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
    public class ReplayIdRequestHandler : IRewardHandler
    {
        private readonly ILogger<ReplayIdRequestHandler> logger;
        private readonly IRewardRequestFactory requestFactory;
        private readonly ITwitchClient twitchClient;
        private readonly IRequestQueue queue;
        private readonly TwitchOptions twitchOptions;

        public ReplayIdRequestHandler(
            ILogger<ReplayIdRequestHandler> logger, 
            IRewardRequestFactory requestFactory, 
            ITwitchClient twitchClient, 
            IRequestQueue queue,
            IOptions<TwitchOptions> twitchOptions)
        {
            this.logger = logger;
            this.requestFactory = requestFactory;
            this.twitchClient = twitchClient;
            this.queue = queue;
            this.twitchOptions = twitchOptions.Value;
        }

        public IEnumerable<RewardType> Supports => new[] { RewardType.ReplayId };

        public void Execute(SupportedReward reward, OnRewardRedeemedArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.Message) && int.TryParse(args.Message.Trim(), out int replayId))
            {
                Task.Factory.StartNew(async () =>
                {
                    RewardResponse response = await queue.EnqueueItemAsync(requestFactory.Create(reward, args));

                    if (twitchOptions.EnableChatBot)
                    {
                        string message = $"{args.DisplayName}, {response.Message}";
                        twitchClient.SendMessage(twitchOptions.Channel, message, dryRun: twitchOptions.DryRunMode);
                    }

                }, TaskCreationOptions.LongRunning);
            }
            else
            {
                twitchClient.SendMessage(twitchOptions.Channel, $"{args.DisplayName}, your request is invalid.", dryRun: twitchOptions.DryRunMode);
                logger.LogDebug($"{args.TimeStamp}: {args.RewardId} - {args.RewardCost}");
            }
        }
    }
}
