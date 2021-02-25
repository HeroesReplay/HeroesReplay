using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Services.Twitch.ChatMessages;
using HeroesReplay.Core.Services.Twitch.RedeemedRewards;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.PubSub.Events;
using HeroesReplay.Core.Services.Shared;
using HeroesReplay.Core.Services.Twitch.Rewards;

namespace HeroesReplay.Core.Services.Twitch
{
    public class FakeTwitchBot : ITwitchBot
    {
        private readonly ILogger<FakeTwitchBot> logger;
        private readonly AppSettings settings;
        private readonly IOnRewardHandler onRewardHandler;
        private readonly IOnMessageHandler onMessageHandler;
        private readonly ISupportedRewardsHolder rewardsHolder;
        private readonly ProcessCancellationTokenProvider tokenProvider;

        public FakeTwitchBot(
            ILogger<FakeTwitchBot> logger,
            AppSettings settings,
            IOnRewardHandler onRewardHandler,
            IOnMessageHandler onMessageHandler,
            ISupportedRewardsHolder rewardsHolder,
            ProcessCancellationTokenProvider tokenProvider)
        {
            this.logger = logger;
            this.settings = settings;
            this.onRewardHandler = onRewardHandler;
            this.onMessageHandler = onMessageHandler;
            this.rewardsHolder = rewardsHolder;
            this.tokenProvider = tokenProvider;
        }

        public async Task InitializeAsync()
        {
            await Task.WhenAll(Task.Run(TriggerOnRewardHandler), Task.Run(TriggerOnMessageHandler));
        }

        private async Task TriggerOnMessageHandler()
        {
            var messages = new ChatMessage[]
            {
                new ChatMessage(null, "userId", "userName", "displayName", "colorHex", System.Drawing.Color.Transparent, null, "!requests", TwitchLib.Client.Enums.UserType.Viewer, "SaltySadism", "id", false, 0, "roomId", false, false, false, false, false, false, false, TwitchLib.Client.Enums.Noisy.NotSet, null, null, null, null, 0, 0),
                new ChatMessage(null, "userId", "userName", "displayName", "colorHex", System.Drawing.Color.Transparent, null, "!requests 1", TwitchLib.Client.Enums.UserType.Viewer, "SaltySadism", "id", false, 0, "roomId", false, false, false, false, false, false, false, TwitchLib.Client.Enums.Noisy.NotSet, null, null, null, null, 0, 0),
                new ChatMessage(null, "userId", "userName", "displayName", "colorHex", System.Drawing.Color.Transparent, null, "!requests me", TwitchLib.Client.Enums.UserType.Viewer, "SaltySadism", "id", false, 0, "roomId", false, false, false, false, false, false, false, TwitchLib.Client.Enums.Noisy.NotSet, null, null, null, null, 0, 0),new ChatMessage(null, "userId", "userName", "displayName", "colorHex", System.Drawing.Color.Transparent, null, "message", TwitchLib.Client.Enums.UserType.Viewer, "SaltySadism", "id", false, 0, "roomId", false, false, false, false, false, false, false, TwitchLib.Client.Enums.Noisy.NotSet, null, null, null, null, 0, 0)
            };

            while (!tokenProvider.Token.IsCancellationRequested)
            {
                foreach (var message in messages)
                {
                    onMessageHandler.Handle(new OnMessageReceivedArgs() { ChatMessage = message });
                }

                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }

        private async Task TriggerOnRewardHandler()
        {
            while (!tokenProvider.Token.IsCancellationRequested)
            {
                foreach (var reward in rewardsHolder.Rewards)
                {
                    onRewardHandler.Handle(new OnRewardRedeemedArgs()
                    {
                        RedemptionId = Guid.NewGuid(),
                        Login = "delegate_",
                        DisplayName = "Delegate_",
                        RewardTitle = reward.Title,
                        Message = "33785849"
                    });

                    await Task.Delay(TimeSpan.FromSeconds(10));
                }
            }
        }
    }
}