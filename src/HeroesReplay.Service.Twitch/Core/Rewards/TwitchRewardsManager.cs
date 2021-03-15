using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomReward;
using TwitchLib.Api.Interfaces;

namespace HeroesReplay.Service.Twitch.Core.Rewards
{
    public class TwitchRewardsManager : ITwitchRewardsManager
    {
        private readonly ILogger<TwitchRewardsManager> logger;
        private readonly ITwitchAPI twitchApi;
        private readonly ICustomRewardsHolder rewardsHolder;
        private readonly LocationOptions locationOptions;
        private readonly TwitchOptions twitchOptions;
        private readonly JsonSerializerOptions options;

        public TwitchRewardsManager(
            ILogger<TwitchRewardsManager> logger, 
            ITwitchAPI twitchApi, 
            ICustomRewardsHolder rewardsHolder,
            IOptions<TwitchOptions> twitchOptions,
            IOptions<LocationOptions> locationOptions)
        {
            this.logger = logger;
            this.twitchApi = twitchApi;
            this.rewardsHolder = rewardsHolder;
            this.locationOptions = locationOptions.Value;
            this.twitchOptions = twitchOptions.Value;
            options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter(allowIntegerValues: true) } };
        }

        public async Task CreateOrUpdateAsync()
        {
            var broadcasterId = await GetChannelId();

            var rewards = await twitchApi.Helix.ChannelPoints.GetCustomReward(broadcasterId);

            var updateList = new List<UpdateCustomRewardResponse>();
            var createList = new List<CreateCustomRewardsResponse>();

            foreach (var reward in rewards.Data)
            {
                await twitchApi.Helix.ChannelPoints.DeleteCustomReward(broadcasterId, reward.Id);
            }

            foreach (SupportedReward supportedReward in rewardsHolder.Rewards)
            {
                try
                {
                    var customReward = rewards.Data.FirstOrDefault(customReward => customReward.Title.Equals(supportedReward.Title));

                    if (customReward != null)
                    {
                        UpdateCustomRewardResponse response = await twitchApi.Helix.ChannelPoints.UpdateCustomReward(broadcasterId, customReward.Id, new UpdateCustomRewardRequest
                        {
                            Title = supportedReward.Title,
                            BackgroundColor = customReward.BackgroundColor,
                            ShouldRedemptionsSkipRequestQueue = supportedReward.ShouldRedemptionsSkipRequestQueue,
                            IsUserInputRequired = supportedReward.IsUserInputRequired,
                            Prompt = supportedReward.Prompt,
                            Cost = supportedReward.Cost,
                            IsEnabled = customReward.IsEnabled,
                            IsMaxPerStreamEnabled = false,
                            IsMaxPerUserPerStreamEnabled = false,
                            IsGlobalCooldownEnabled = true,
                            GlobalCooldownSeconds = Convert.ToInt32(TimeSpan.FromHours(1).TotalSeconds),


                        }, twitchOptions.AccessToken);

                        updateList.Add(response);
                    }
                    else
                    {
                        CreateCustomRewardsResponse response = await twitchApi.Helix.ChannelPoints.CreateCustomRewards(broadcasterId, new CreateCustomRewardsRequest
                        {
                            Title = supportedReward.Title,
                            Prompt = supportedReward.Prompt,
                            ShouldRedemptionsSkipRequestQueue = supportedReward.ShouldRedemptionsSkipRequestQueue,
                            IsUserInputRequired = supportedReward.IsUserInputRequired,
                            Cost = supportedReward.Cost,
                            IsGlobalCooldownEnabled = true,
                            GlobalCooldownSeconds = Convert.ToInt32(TimeSpan.FromHours(1).TotalSeconds),
                            IsEnabled = true
                        }, twitchOptions.AccessToken);

                        createList.Add(response);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Could not create or update: {supportedReward.Title}");
                }
            }
        }

        public async Task GenerateAsync()
        {
            var path = Path.Combine(locationOptions.DataDirectory, "custom-rewards.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize<IEnumerable<SupportedReward>>(rewardsHolder.Rewards, options));
        }

        private async Task<string> GetChannelId()
        {
            var userResponse = await twitchApi.Helix.Users.GetUsersAsync(logins: new List<string> { twitchOptions.Channel });
            var channelId = userResponse.Users[0].Id;
            return channelId;
        }
    }
}
