using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Twitch
{
    public class ReplayRequestQueue : IReplayRequestQueue
    {
        private readonly FileInfo fileInfo;
        private readonly ILogger<ReplayRequestQueue> logger;
        private readonly IHeroesProfileService heroesProfileService;
        private readonly AppSettings settings;

        public ReplayRequestQueue(ILogger<ReplayRequestQueue> logger, IHeroesProfileService heroesProfileService, AppSettings settings)
        {
            fileInfo = new FileInfo(Path.Combine(settings.AssetsPath, settings.Twitch.RequestsFileName));
            this.logger = logger;
            this.heroesProfileService = heroesProfileService;
            this.settings = settings;
        }

        public async Task<int> GetTotalQueuedItems()
        {
            if (fileInfo.Exists)
            {
                List<RewardQueueItem> requests = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName));
                return requests.Count;
            }

            return 0;
        }

        public async Task<RewardResponse> EnqueueRequestAsync(RewardRequest request)
        {
            try
            {
                if (request.ReplayId.HasValue)
                {
                    return await CreateRewardResponseByReplayId(request);
                }
                else
                {
                    return await CreateRewardResponseByRequest(request);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not queue request");
                return new RewardResponse(success: false, message: e.Message);
            }
        }

        private async Task<RewardResponse> CreateRewardResponseByReplayId(RewardRequest request)
        {
            HeroesProfileReplay heroesProfileReplay = await heroesProfileService.GetReplayAsync(request.ReplayId.Value);

            if (heroesProfileReplay == null)
            {
                return new RewardResponse(success: false, message: $"could not find replay with id {request.ReplayId.Value}");
            }
            else
            {
                if (heroesProfileReplay.Deleted != null)
                {
                    return new RewardResponse(success: false, message: $"the raw file for replay id {request.ReplayId.Value} is no longer available.");
                }

                if (!heroesProfileReplay.GameVersion.Equals(settings.Spectate.VersionSupported.ToString()))
                {
                    return new RewardResponse(success: false, message: $"version found {heroesProfileReplay.GameVersion} but expected {settings.Spectate.VersionSupported}");
                }

                var item = new RewardQueueItem(request, null);

                if (!fileInfo.Exists)
                {
                    await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(new List<RewardQueueItem>() { item }, new JsonSerializerOptions { WriteIndented = true }));
                    return new RewardResponse(success: true, message: $"{request.ReplayId.Value} in queue ({1})");
                }
                else
                {
                    List<RewardQueueItem> items = new List<RewardQueueItem>(JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName))) { item };
                    await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
                    return new RewardResponse(success: true, message: $"{request.ReplayId.Value} in queue ({items.Count})");
                }
            }
        }

        private async Task<RewardResponse> CreateRewardResponseByRequest(RewardRequest request)
        {
            // Map Request
            RewardReplay rewardReplay = await heroesProfileService.GetReplayAsync(mode: request.GameMode, tier: request.Tier, map: request.Map);

            if (rewardReplay != null)
            {
                var item = new RewardQueueItem(request, rewardReplay);

                if (!fileInfo.Exists)
                {
                    await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(new List<RewardQueueItem>() { item }, new JsonSerializerOptions { WriteIndented = true }));

                    if (string.IsNullOrWhiteSpace(request.Map))
                    {
                        return new RewardResponse(success: true, message: $"Request '{request.RewardTitle}' queued: {item.Replay.Map} ({1})");
                    }
                    else
                    {
                        return new RewardResponse(success: true, message: $"Request '{request.RewardTitle}' queued ({1})");
                    }
                }
                else
                {
                    List<RewardQueueItem> items = new List<RewardQueueItem>(JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName))) { item };
                    await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));

                    if (string.IsNullOrWhiteSpace(request.Map))
                    {
                        return new RewardResponse(success: true, message: $"Request '{request.RewardTitle}' queued: {item.Replay.Map} ({items.Count})");
                    }
                    else
                    {
                        return new RewardResponse(success: true, message: $"Request '{request.RewardTitle}' queued ({items.Count})");
                    }
                }
            }
            else
            {
                return new RewardResponse(success: false, message: "Request failed to queue because the given reward criteria could not be found");
            }
        }

        public async Task<RewardQueueItem> GetNextRewardQueueItem()
        {
            if (fileInfo.Exists)
            {
                List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName));

                if (items.Count > 0)
                {
                    RewardQueueItem item = items[0];

                    if (items.Remove(item))
                    {
                        await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
                        logger.LogInformation($"Request: '{item.Request.RewardTitle}' removed from the queue.");
                        return item;
                    }
                }
            }

            return null;
        }
    }
}
