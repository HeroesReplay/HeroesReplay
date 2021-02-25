using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;

using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Twitch.Rewards
{
    public class ReplayRequestQueue : IRequestQueue
    {
        private readonly FileInfo fileInfo;
        private readonly ILogger<ReplayRequestQueue> logger;
        private readonly IHeroesProfileService heroesProfileService;
        private readonly AppSettings settings;
        private readonly JsonSerializerOptions options;

        public ReplayRequestQueue(ILogger<ReplayRequestQueue> logger, IHeroesProfileService heroesProfileService, AppSettings settings)
        {
            fileInfo = new FileInfo(Path.Combine(settings.AssetsPath, settings.Twitch.RequestsFileName));
            this.logger = logger;
            this.heroesProfileService = heroesProfileService;
            this.settings = settings;
            this.options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter(allowIntegerValues: true) } };
        }

        public async Task<int> GetItemsInQueue()
        {
            if (fileInfo.Exists)
            {
                List<RewardQueueItem> requests = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName), options);
                return requests.Count;
            }

            return 0;
        }

        public async Task<RewardResponse> EnqueueItemAsync(RewardRequest request)
        {
            try
            {
                if (request.ReplayId.HasValue)
                {
                    return await QueueByReplayIdAsync(request);
                }
                else
                {
                    return await QueueByRewardFilterAsync(request);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not queue request");
                return new RewardResponse(success: false, message: e.Message);
            }
        }

        private async Task<RewardResponse> QueueByReplayIdAsync(RewardRequest request)
        {
            HeroesProfileReplay replay = await heroesProfileService.GetReplayByIdAsync(request.ReplayId.Value);

            if (replay == null)
            {
                return new RewardResponse(success: false, message: $"could not find replay with id {request.ReplayId.Value}");
            }
            else
            {
                if (replay.Deleted != null)
                {
                    return new RewardResponse(success: false, message: $"the raw file for replay id {request.ReplayId.Value} is no longer available.");
                }

                if (!replay.GameVersion.Equals(settings.Spectate.VersionSupported))
                {
                    return new RewardResponse(success: false, message: $"version found {replay.GameVersion} but only support {settings.Spectate.VersionSupported}");
                }

                RewardQueueItem item = new RewardQueueItem(request, replay);

                if (!fileInfo.Exists)
                {
                    await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(new List<RewardQueueItem>() { item }, options));
                    return new RewardResponse(success: true, message: $"{request.ReplayId.Value} in queue ({1})");
                }
                else
                {
                    List<RewardQueueItem> items = new List<RewardQueueItem>(JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName), options)) { item };
                    await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(items, options));
                    return new RewardResponse(success: true, message: $"{request.ReplayId.Value} in queue ({items.Count})");
                }
            }
        }

        private async Task<RewardResponse> QueueByRewardFilterAsync(RewardRequest request)
        {
            // Map Request
            IEnumerable<HeroesProfileReplay> replays = await heroesProfileService.GetReplaysByFilters(request.GameType, request.Rank, request.Map);
            HeroesProfileReplay replay = replays.OrderBy(x => Guid.NewGuid()).FirstOrDefault();

            if (replay != null)
            {
                var item = new RewardQueueItem(request, replay);

                if (!fileInfo.Exists)
                {
                    await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(new List<RewardQueueItem>() { item }, options));

                    if (string.IsNullOrWhiteSpace(request.Map))
                    {
                        return new RewardResponse(success: true, message: $"Request '{request.RewardTitle}' queued: {item.HeroesProfileReplay.Map} ({1})");
                    }
                    else
                    {
                        return new RewardResponse(success: true, message: $"Request '{request.RewardTitle}' queued ({1})");
                    }
                }
                else
                {
                    List<RewardQueueItem> items = new List<RewardQueueItem>(JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName), options)) { item };
                    await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(items, options));

                    if (string.IsNullOrWhiteSpace(request.Map))
                    {
                        return new RewardResponse(success: true, message: $"Request '{request.RewardTitle}' queued: {item.HeroesProfileReplay.Map} ({items.Count})");
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

        public async Task<RewardQueueItem> DequeueItemAsync()
        {
            if (fileInfo.Exists)
            {
                List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName), options);

                if (items.Count > 0)
                {
                    RewardQueueItem item = items[0];

                    if (items.Remove(item))
                    {
                        await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(items, options));
                        logger.LogInformation($"Request: '{item.Request.RewardTitle}' removed from the queue.");
                        return item;
                    }
                }
            }

            return null;
        }

        public async Task<(RewardQueueItem, int Position)?> FindNextByLoginAsync(string login)
        {
            if (fileInfo.Exists)
            {
                List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName), options);

                var item = items.FirstOrDefault(x => x.Request.Login.Equals(login, StringComparison.OrdinalIgnoreCase));

                if (item != null)
                {
                    return (item, items.IndexOf(item) + 1);
                }
            }

            return null;
        }

        public async Task<RewardQueueItem> FindByIndexAsync(int index)
        {
            if (fileInfo.Exists)
            {
                List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName), options);

                if (items.Count > 0)
                {
                    RewardQueueItem item = items.Find(item => items.IndexOf(item) == (index - 1));

                    if (item != null)
                    {
                        return item;
                    }
                }
            }

            return null;
        }
    }
}
