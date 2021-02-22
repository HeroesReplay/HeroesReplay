using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;

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
        private readonly IHeroesProfileService heroesProfileService;
        private readonly AppSettings settings;

        public ReplayRequestQueue(IHeroesProfileService heroesProfileService, AppSettings settings)
        {
            fileInfo = new FileInfo(Path.Combine(settings.AssetsPath, settings.Twitch.ReplayRequestsFileName));
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
                // Replay ID Request
                if (request.ReplayId.HasValue)
                {
                    HeroesProfileReplay heroesProfileReplay = await heroesProfileService.GetReplayAsync(request.ReplayId.Value);

                    if (heroesProfileReplay == null)
                    {
                        return new RewardResponse(success: false, message: $"Could not find: {request.ReplayId.Value}");
                    }

                    if (heroesProfileReplay != null)
                    {
                        if (heroesProfileReplay.Deleted != null)
                        {
                            return new RewardResponse(success: false, message: $"The raw file for {request.ReplayId.Value} is no longer available.");
                        }

                        if (!heroesProfileReplay.GameVersion.Equals(settings.Spectate.VersionSupported.ToString()))
                        {
                            return new RewardResponse(success: false, message: $"Version found: {heroesProfileReplay.GameVersion} but expected {settings.Spectate.VersionSupported}.");
                        }
                    }

                    if (heroesProfileReplay != null)
                    {
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
                else
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
                }

                return new RewardResponse(success: false, message: "Request failed to queue because it's not handled.");
            }
            catch (Exception e)
            {
                return new RewardResponse(success: false, message: e.Message);
            }
        }

        public async Task<RewardQueueItem> GetNextRewardQueueItem()
        {
            if (fileInfo.Exists)
            {
                List<RewardQueueItem> requests = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName));

                if (requests.Count > 0)
                {
                    RewardQueueItem request = requests[0];

                    if (requests.Remove(request))
                    {
                        await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(requests, new JsonSerializerOptions { WriteIndented = true }));
                        return request;
                    }
                }
            }

            return null;
        }
    }
}
