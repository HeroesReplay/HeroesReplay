using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;

using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Twitch.Rewards
{
    public class ReplayRequestQueue : IRequestQueue, IDisposable
    {
        private readonly FileInfo fileInfo;
        private readonly ILogger<ReplayRequestQueue> logger;
        private readonly IHeroesProfileService heroesProfileService;
        private readonly AppSettings settings;
        private readonly JsonSerializerOptions options;

        private readonly static ReaderWriterLockSlim locker = new ReaderWriterLockSlim();

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
                try
                {
                    locker.EnterReadLock();
                    List<RewardQueueItem> requests = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName), options);
                    return requests.Count;
                }
                finally
                {
                    locker.ExitReadLock();
                }
            }

            return 0;
        }

        public async Task<RewardResponse> EnqueueItemAsync(RewardRequest request)
        {
            try
            {
                locker.EnterWriteLock();

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
                return new RewardResponse(success: false, message: "there was an unexpected error with your request.");
            }
            finally
            {
                locker.ExitWriteLock();
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
                    return new RewardResponse(success: false, message: $"the version found '{replay.GameVersion}' does not match the supported version '{settings.Spectate.VersionSupported}'");
                }

                RewardQueueItem item = new RewardQueueItem(request, replay);

                if (!fileInfo.Exists)
                {
                    await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(new List<RewardQueueItem>() { item }, options));
                    return new RewardResponse(success: true, message: $"{request.ReplayId.Value} - {replay.Map} ({replay.Rank}) has been queued. ({1})");
                }
                else
                {
                    List<RewardQueueItem> items = new List<RewardQueueItem>(JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName), options)) { item };
                    await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(items, options));
                    return new RewardResponse(success: true, message: $"{request.ReplayId.Value} - {replay.Map} ({replay.Rank}) has been queued. ({items.Count})");
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
                    return new RewardResponse(success: true, message: $"'{request.RewardTitle}' - {replay.Map} ({replay.Rank}) has been queued ({1})");
                }
                else
                {
                    List<RewardQueueItem> items = new List<RewardQueueItem>(JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName), options)) { item };
                    await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(items, options));
                    return new RewardResponse(success: true, message: $"'{request.RewardTitle}' - {replay.Map} ({replay.Rank}) has been queued ({items.Count})");
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
                try
                {
                    locker.EnterWriteLock();
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
                finally
                {
                    locker.ExitWriteLock();
                }
            }

            return null;
        }

        public async Task<(RewardQueueItem Item, int Position)?> RemoveItemAsync(string login)
        {
            if (fileInfo.Exists)
            {
                try
                {
                    locker.EnterWriteLock();
                    List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName), options);

                    if (items.Count > 0)
                    {
                        RewardQueueItem item = items.Find(item => item.Request.Login.Equals(login, StringComparison.OrdinalIgnoreCase));

                        if (item != null)
                        {
                            int position = items.IndexOf(item) + 1;

                            if (items.Remove(item))
                            {
                                await File.WriteAllTextAsync(fileInfo.FullName, JsonSerializer.Serialize(items, options));
                                logger.LogInformation($"Request: '{item.Request.RewardTitle}' removed from the queue.");
                                return (item, position);
                            }
                        }
                    }
                }
                finally
                {
                    locker.ExitWriteLock();
                }
            }

            return null;
        }

        public async Task<(RewardQueueItem Item, int Position)?> FindNextByLoginAsync(string login)
        {
            if (fileInfo.Exists)
            {
                try
                {
                    locker.EnterReadLock();

                    List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(fileInfo.FullName), options);

                    var item = items.FirstOrDefault(x => x.Request.Login.Equals(login, StringComparison.OrdinalIgnoreCase));

                    if (item != null)
                    {
                        return (item, items.IndexOf(item) + 1);
                    }
                }
                finally
                {
                    locker.ExitReadLock();
                }
            }

            return null;
        }

        public async Task<RewardQueueItem> FindByIndexAsync(int index)
        {
            if (fileInfo.Exists)
            {
                try
                {
                    locker.EnterReadLock();

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
                finally
                {
                    locker.ExitReadLock();
                }
            }

            return null;
        }

        public void Dispose()
        {
            try
            {
                locker.Dispose();
            }
            catch
            {

            }
        }
    }
}
