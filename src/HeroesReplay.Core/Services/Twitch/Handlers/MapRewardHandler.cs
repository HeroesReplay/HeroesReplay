using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;

using System.Collections.Generic;
using System.Threading.Tasks;

using TwitchLib.Client.Interfaces;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.RewardHandlers
{
    public class MapRewardHandler : IRewardHandler
    {
        private readonly ILogger<MapRewardHandler> logger;
        private readonly ITwitchClient twitchClient;
        private readonly IReplayRequestQueue queue;
        private readonly AppSettings settings;

        public MapRewardHandler(ILogger<MapRewardHandler> logger, ITwitchClient twitchClient, IReplayRequestQueue queue, AppSettings settings)
        {
            this.logger = logger;
            this.twitchClient = twitchClient;
            this.queue = queue;
            this.settings = settings;
        }

        private readonly RewardType[] Supported = new[]
        {
            RewardType.ARAM,
            RewardType.ARAMMap,
            RewardType.ARAMTier,
            RewardType.ARAMMapTier,

            RewardType.QM,
            RewardType.QMMap,
            RewardType.QMTier,
            RewardType.QMMapTier,

            RewardType.UD,
            RewardType.UDMap,
            RewardType.UDTier,
            RewardType.UDMapTier,

            RewardType.SL,
            RewardType.SLMap,
            RewardType.SLTier,
            RewardType.SLMapTier
        };

        public IEnumerable<RewardType> Supports => Supported;

        public void Execute(SupportedReward reward, OnRewardRedeemedArgs args)
        {
            Task.Factory.StartNew(async () =>
            {
                logger.LogInformation($"{args.Login} has redeemed {args.RewardTitle}");

                RewardResponse response = await queue.EnqueueRequestAsync(new RewardRequest(login: args.Login, rewardTitle: reward.RewardTitle, replayId: null, tier: reward.Tier, map: reward.Map, gameMode: reward.Mode));

                if (settings.Twitch.EnableChatBot)
                {
                    twitchClient.SendMessage(settings.Twitch.Channel, $"{args.DisplayName}, {response.Message}", dryRun: settings.Twitch.DryRunMode);
                }

            }, TaskCreationOptions.LongRunning);
        }
    }
}
