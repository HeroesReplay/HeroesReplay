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
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomReward;
using TwitchLib.Api.Interfaces;

namespace HeroesReplay.Core.Services.Twitch.Rewards;

public class TwitchRewardsManager : ITwitchRewardsManager
{
    private readonly ILogger<TwitchRewardsManager> logger;
    private readonly ITwitchAPI twitchApi;
    private readonly ICustomRewardsHolder rewardsHolder;
    private readonly AppSettings settings;
    private readonly JsonSerializerOptions options;

    public TwitchRewardsManager(
        ILogger<TwitchRewardsManager> logger,
        ITwitchAPI twitchApi,
        ICustomRewardsHolder rewardsHolder,
        AppSettings settings
    )
    {
        this.logger = logger;
        this.twitchApi = twitchApi;
        this.rewardsHolder = rewardsHolder;
        this.settings = settings;
        options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: true) },
        };
    }

    public async Task CreateOrUpdateAsync()
    {
        UnrankedDraftRewardRemoval removed = await DeleteUnrankedDraftRewardsAsync();
        if (removed.Deleted.Count > 0 || removed.Failed.Count > 0)
        {
            logger.LogInformation(
                "Unranked Draft channel rewards: deleted {Deleted}, failed {Failed}.",
                removed.Deleted.Count,
                removed.Failed.Count
            );
        }

        var broadcasterId = await GetChannelId();

        var rewards = await twitchApi.Helix.ChannelPoints.GetCustomRewardAsync(broadcasterId);
        var existing =
            rewards?.Data ?? Array.Empty<TwitchLib.Api.Helix.Models.ChannelPoints.CustomReward>();

        var updateList = new List<UpdateCustomRewardResponse>();
        var createList = new List<CreateCustomRewardsResponse>();

        foreach (SupportedReward supportedReward in rewardsHolder.Rewards)
        {
            try
            {
                var customReward = existing.FirstOrDefault(customReward =>
                    customReward.Title.Equals(supportedReward.Title)
                );

                if (customReward != null)
                {
                    UpdateCustomRewardResponse response =
                        await twitchApi.Helix.ChannelPoints.UpdateCustomRewardAsync(
                            broadcasterId,
                            customReward.Id,
                            new UpdateCustomRewardRequest
                            {
                                Title = supportedReward.Title,
                                BackgroundColor = customReward.BackgroundColor,
                                ShouldRedemptionsSkipRequestQueue =
                                    supportedReward.ShouldRedemptionsSkipRequestQueue,
                                IsUserInputRequired = supportedReward.IsUserInputRequired,
                                Prompt = supportedReward.Prompt,
                                Cost = supportedReward.Cost,
                                IsEnabled = customReward.IsEnabled,
                                IsMaxPerStreamEnabled = false,
                                IsMaxPerUserPerStreamEnabled = false,
                                IsGlobalCooldownEnabled = true,
                                GlobalCooldownSeconds = Convert.ToInt32(
                                    TimeSpan.FromHours(1).TotalSeconds
                                ),
                            },
                            settings.Twitch.AccessToken
                        );

                    updateList.Add(response);
                }
                else
                {
                    CreateCustomRewardsResponse response =
                        await twitchApi.Helix.ChannelPoints.CreateCustomRewardsAsync(
                            broadcasterId,
                            new CreateCustomRewardsRequest
                            {
                                Title = supportedReward.Title,
                                Prompt = supportedReward.Prompt,
                                ShouldRedemptionsSkipRequestQueue =
                                    supportedReward.ShouldRedemptionsSkipRequestQueue,
                                IsUserInputRequired = supportedReward.IsUserInputRequired,
                                Cost = supportedReward.Cost,
                                IsGlobalCooldownEnabled = true,
                                GlobalCooldownSeconds = Convert.ToInt32(
                                    TimeSpan.FromHours(1).TotalSeconds
                                ),
                                IsEnabled = true,
                            },
                            settings.Twitch.AccessToken
                        );

                    createList.Add(response);
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    e,
                    "Could not create or update '{Title}'. Helix only edits rewards created with this Client-Id.",
                    supportedReward.Title
                );
            }
        }

        logger.LogInformation(
            "Channel rewards: created {Created}, updated {Updated}, catalog {Catalog}.",
            createList.Count,
            updateList.Count,
            rewardsHolder.Rewards.Count
        );
    }

    public async Task<IReadOnlyList<string>> ListRemoteTitlesAsync()
    {
        var broadcasterId = await GetChannelId();
        var rewards = await twitchApi.Helix.ChannelPoints.GetCustomRewardAsync(broadcasterId);
        if (rewards?.Data == null)
        {
            return Array.Empty<string>();
        }

        return rewards.Data.Select(r => r.Title).ToArray();
    }

    public async Task<UnrankedDraftRewardRemoval> DeleteUnrankedDraftRewardsAsync()
    {
        var broadcasterId = await GetChannelId();
        var rewards = await twitchApi.Helix.ChannelPoints.GetCustomRewardAsync(
            broadcasterId,
            accessToken: settings.Twitch.AccessToken
        );
        var existing =
            rewards?.Data ?? Array.Empty<TwitchLib.Api.Helix.Models.ChannelPoints.CustomReward>();
        var deleted = new List<string>();
        var failed = new List<string>();

        foreach (var reward in existing)
        {
            if (!UnrankedDraftRewardTitles.IsUnrankedDraft(reward.Title))
            {
                continue;
            }

            try
            {
                await twitchApi.Helix.ChannelPoints.DeleteCustomRewardAsync(
                    broadcasterId,
                    reward.Id,
                    settings.Twitch.AccessToken
                );
                deleted.Add(reward.Title);
                logger.LogInformation("Deleted Unranked Draft reward '{Title}'.", reward.Title);
            }
            catch (Exception e)
            {
                failed.Add(reward.Title);
                logger.LogWarning(
                    "Could not delete Unranked Draft reward '{Title}': {Error}",
                    reward.Title,
                    SafeError(e)
                );
            }
        }

        return new UnrankedDraftRewardRemoval(deleted, failed);
    }

    private static string SafeError(Exception exception)
    {
        string message = exception.Message ?? string.Empty;
        if (
            message.Contains("Bearer ", StringComparison.OrdinalIgnoreCase)
            || message.Contains("oauth", StringComparison.OrdinalIgnoreCase)
            || message.Contains("access_token", StringComparison.OrdinalIgnoreCase)
        )
        {
            return exception.GetType().Name;
        }

        return exception.GetType().Name + ": " + message;
    }

    public async Task GenerateAsync()
    {
        var path = Path.Combine(settings.Location.DataDirectory, "custom-rewards.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize<IEnumerable<SupportedReward>>(rewardsHolder.Rewards, options)
        );
    }

    private async Task<string> GetChannelId()
    {
        var userResponse = await twitchApi.Helix.Users.GetUsersAsync(
            logins: new List<string> { settings.Twitch.Channel }
        );
        var channelId = userResponse.Users[0].Id;
        return channelId;
    }
}
