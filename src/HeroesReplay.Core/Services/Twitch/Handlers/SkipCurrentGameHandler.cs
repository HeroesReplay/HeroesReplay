
using HeroesReplay.Core.Configuration;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;

using TwitchLib.Client.Interfaces;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.RewardHandlers
{
    public class SkipCurrentGameHandler : IRewardHandler
    {
        private readonly ILogger<SkipCurrentGameHandler> logger;
        private readonly ISessionHolder sessionHolder;
        private readonly ITwitchClient twitchClient;
        private readonly AppSettings settings;

        public IEnumerable<RewardType> Supports => new[] { RewardType.SkipCurrent };

        public SkipCurrentGameHandler(ILogger<SkipCurrentGameHandler> logger, ISessionHolder sessionHolder, ITwitchClient twitchClient, AppSettings settings)
        {
            this.logger = logger;
            this.sessionHolder = sessionHolder;
            this.twitchClient = twitchClient;
            this.settings = settings;
        }

        public void Execute(SupportedReward reward, OnRewardRedeemedArgs args)
        {
            try
            {
                if (sessionHolder.Current != null && sessionHolder.Current.ViewerCancelRequestSource != null && !sessionHolder.Current.ViewerCancelRequestSource.IsCancellationRequested)
                {
                    sessionHolder.Current.ViewerCancelRequestSource.Cancel();

                    twitchClient.SendMessage(settings.Twitch.Channel, $"{args.Login}, exiting the current replay.", settings.Twitch.DryRunMode);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not cancel the current session.");
            }
        }
    }
}
