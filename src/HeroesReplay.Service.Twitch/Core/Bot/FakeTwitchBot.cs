using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;
using HeroesReplay.Service.Twitch.Core.ChatMessages;
using HeroesReplay.Service.Twitch.Core.RedeemedRewards;
using HeroesReplay.Service.Twitch.Core.Rewards;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Service.Twitch.Core.Bot
{
    public class FakeTwitchBot : ITwitchBot
    {
        private readonly ILogger<FakeTwitchBot> logger;
        private readonly IOnRewardHandler onRewardHandler;
        private readonly IOnMessageHandler onMessageHandler;
        private readonly ICustomRewardsHolder rewardsHolder;
        private readonly CancellationTokenSource cts;

        public FakeTwitchBot(
            ILogger<FakeTwitchBot> logger,
            IOnRewardHandler onRewardHandler,
            IOnMessageHandler onMessageHandler,
            ICustomRewardsHolder rewardsHolder,
            CancellationTokenSource cts)
        {
            this.logger = logger;
            this.onRewardHandler = onRewardHandler;
            this.onMessageHandler = onMessageHandler;
            this.rewardsHolder = rewardsHolder;
            this.cts = cts;
        }

        public async Task InitializeAsync()
        {
            await Task.WhenAll(Task.Run(TriggerOnRewardHandler), Task.Run(TriggerOnMessageHandler));
        }

        public async Task StartAsync()
        {
            await InitializeAsync();
        }

        public Task StopAsync()
        {
            return Task.CompletedTask;
        }

        private async Task TriggerOnMessageHandler()
        {
            var messages = new ChatMessage[]
            {
                new ChatMessage(null, "userId", "delegate_", "Delegate_", "colorHex", System.Drawing.Color.Transparent, null, "!requests", TwitchLib.Client.Enums.UserType.Viewer, "SaltySadism", "id", false, 0, "roomId", false, false, false, false, false, false, false, TwitchLib.Client.Enums.Noisy.NotSet, null, null, null, null, 0, 0),
                new ChatMessage(null, "userId", "delegate_", "Delegate_", "colorHex", System.Drawing.Color.Transparent, null, "!requests 1", TwitchLib.Client.Enums.UserType.Viewer, "SaltySadism", "id", false, 0, "roomId", false, false, false, false, false, false, false, TwitchLib.Client.Enums.Noisy.NotSet, null, null, null, null, 0, 0),
                new ChatMessage(null, "userId", "delegate_", "Delegate_", "colorHex", System.Drawing.Color.Transparent, null, "!requests me", TwitchLib.Client.Enums.UserType.Viewer, "SaltySadism", "id", false, 0, "roomId", false, false, false, false, false, false, false, TwitchLib.Client.Enums.Noisy.NotSet, null, null, null, null, 0, 0),new ChatMessage(null, "userId", "userName", "displayName", "colorHex", System.Drawing.Color.Transparent, null, "message", TwitchLib.Client.Enums.UserType.Viewer, "SaltySadism", "id", false, 0, "roomId", false, false, false, false, false, false, false, TwitchLib.Client.Enums.Noisy.NotSet, null, null, null, null, 0, 0)
            };

            while (!cts.Token.IsCancellationRequested)
            {
                foreach (var message in messages)
                {
                    onMessageHandler.Handle(new OnMessageReceivedArgs() { ChatMessage = message });

                    logger.LogDebug("waiting to send messages...");
                    await Task.Delay(TimeSpan.FromSeconds(10));
                }
            }
        }

        private async Task TriggerOnRewardHandler()
        {
            while (!cts.Token.IsCancellationRequested)
            {
                foreach (var reward in rewardsHolder.Rewards)
                {
                    string message = null;

                    if (reward.RewardType == RewardType.ReplayId)
                        message = "33785849";
                    if (reward.RewardType.HasFlag(RewardType.Rank))
                        message = Enum.GetName(typeof(GameRank), Enum.GetValues(typeof(GameRank)).Cast<GameRank>().OrderBy(x => Guid.NewGuid()).First());

                    onRewardHandler.Handle(new OnRewardRedeemedArgs()
                    {
                        RedemptionId = Guid.NewGuid(),
                        Login = "delegate_",
                        DisplayName = "Delegate_",
                        RewardTitle = reward.Title,
                        Message = message
                    });

                    await Task.Delay(TimeSpan.FromSeconds(10));
                }

                logger.LogDebug("waiting to send reward deemed...");
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }
    }
}