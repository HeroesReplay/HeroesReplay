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
using HeroesReplay.Core.Services.Twitch.Rewards;

using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Queue
{
    public class RequestQueue : IRequestQueue, IDisposable
    {
        private readonly FileInfo queueFile;
        private readonly FileInfo failedFile;
        private readonly ILogger<RequestQueue> logger;
        private readonly IHeroesProfileService heroesProfileService;
        private readonly AppSettings settings;
        private readonly JsonSerializerOptions options;
        private readonly SemaphoreSlim successSemaphore;
        private readonly SemaphoreSlim failedSemaphore;

        public RequestQueue(ILogger<RequestQueue> logger, IHeroesProfileService heroesProfileService, AppSettings settings)
        {
            this.logger = logger;
            this.heroesProfileService = heroesProfileService;
            this.settings = settings;

            queueFile = new FileInfo(Path.Combine(settings.AssetsPath, settings.Twitch.QueueFileName));
            failedFile = new FileInfo(Path.Combine(settings.AssetsPath, settings.Twitch.FailedFileName));
            options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter(allowIntegerValues: true) } };
            successSemaphore = new SemaphoreSlim(1, maxCount: 1);
            failedSemaphore = new SemaphoreSlim(1, maxCount: 1);
        }

        public async Task<int> GetItemsInQueue()
        {
            if (queueFile.Exists)
            {
                try
                {
                    await successSemaphore.WaitAsync();
                    List<RewardQueueItem> requests = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(queueFile.FullName), options);
                    successSemaphore.Release();
                    return requests.Count;
                }
                finally
                {
                    successSemaphore.Release();
                }
            }

            return 0;
        }

        public async Task<RewardResponse> EnqueueItemAsync(RewardRequest request)
        {
            try
            {
                await successSemaphore.WaitAsync();

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
                successSemaphore.Release();
            }
        }

        private async Task<RewardResponse> QueueByReplayIdAsync(RewardRequest request)
        {
            HeroesProfileReplay replay = await heroesProfileService.GetReplayByIdAsync(request.ReplayId.Value);

            if (replay == null)
            {
                await AddToFailedRequestsAsync(new RewardQueueItem(request, replay));
                return new RewardResponse(success: false, message: $"could not find replay with id {request.ReplayId.Value}");
            }

            if (replay.Deleted != null)
            {
                await AddToFailedRequestsAsync(new RewardQueueItem(request, replay));
                return new RewardResponse(success: false, message: $"the raw file for replay id {request.ReplayId.Value} is no longer available.");
            }

            if (!replay.GameVersion.Equals(settings.Spectate.VersionSupported))
            {
                await AddToFailedRequestsAsync(new RewardQueueItem(request, replay));
                return new RewardResponse(success: false, message: $"the version found '{replay.GameVersion}' does not match the supported version '{settings.Spectate.VersionSupported}'");
            }

            int position = await QueueReplayId(new RewardQueueItem(request, replay));
            return new RewardResponse(success: true, message: $"{replay.Id} - {replay.Map} ({replay.Rank}) has been queued. ({position})");
        }

        private async Task<int> QueueReplayId(RewardQueueItem item)
        {
            if (!queueFile.Exists)
            {
                await File.WriteAllTextAsync(queueFile.FullName, JsonSerializer.Serialize(new List<RewardQueueItem> { item }, options));
                return 1;
            }
            else
            {
                List<RewardQueueItem> items = new List<RewardQueueItem>(JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(queueFile.FullName), options)) { item };
                await File.WriteAllTextAsync(queueFile.FullName, JsonSerializer.Serialize(items, options));
                return items.Count;
            }
        }

        private async Task AddToFailedRequestsAsync(RewardQueueItem item)
        {
            await failedSemaphore.WaitAsync();

            if (failedFile.Exists)
            {
                List<RewardQueueItem> items = new List<RewardQueueItem>(JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(failedFile.FullName), options)) { item };
                await File.WriteAllTextAsync(failedFile.FullName, JsonSerializer.Serialize(items, options));
            }
            else
            {
                await File.WriteAllTextAsync(failedFile.FullName, JsonSerializer.Serialize(new List<RewardQueueItem> { item }, options));
            }

            failedSemaphore.Release();
        }

        private async Task<RewardResponse> QueueByRewardFilterAsync(RewardRequest request)
        {
            IEnumerable<HeroesProfileReplay> replays = await heroesProfileService.GetReplaysByFilters(request.GameType, request.Rank, request.Map);
            HeroesProfileReplay replay = replays.OrderBy(x => Guid.NewGuid()).FirstOrDefault();

            if (replay != null)
            {
                int position = await QueueReplayId(new RewardQueueItem(request, replay));
                return new RewardResponse(success: true, message: $"'{request.RewardTitle}' - {replay.Map} ({replay.Rank}) has been queued ({position})");
            }
            else
            {
                await AddToFailedRequestsAsync(new RewardQueueItem(request, replay));
                return new RewardResponse(success: false, message: "Request failed to queue because the given reward criteria could not be found");
            }
        }

        public async Task<RewardQueueItem> DequeueItemAsync()
        {
            if (queueFile.Exists)
            {
                try
                {
                    await successSemaphore.WaitAsync();
                    List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(queueFile.FullName), options);

                    if (items.Count > 0)
                    {
                        RewardQueueItem item = items[0];

                        if (items.Remove(item))
                        {
                            await File.WriteAllTextAsync(queueFile.FullName, JsonSerializer.Serialize(items, options));
                            logger.LogInformation($"Request: '{item.Request.RewardTitle}' removed from the queue.");
                            return item;
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not dequeue item");
                }
                finally
                {
                    successSemaphore.Release();
                }
            }

            return null;
        }

        public async Task<(RewardQueueItem Item, int Position)?> RemoveItemAsync(string login)
        {
            if (queueFile.Exists)
            {
                try
                {
                    await successSemaphore.WaitAsync();
                    List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(queueFile.FullName), options);

                    if (items.Count > 0)
                    {
                        RewardQueueItem item = items.Find(item => item.Request.Login.Equals(login, StringComparison.OrdinalIgnoreCase));

                        if (item != null)
                        {
                            int position = items.IndexOf(item) + 1;

                            if (items.Remove(item))
                            {
                                await File.WriteAllTextAsync(queueFile.FullName, JsonSerializer.Serialize(items, options));
                                logger.LogInformation($"Request: '{item.Request.RewardTitle}' removed from the queue.");
                                return (item, position);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not remove item from queue");
                }
                finally
                {
                    successSemaphore.Release();
                }
            }

            return null;
        }

        public async Task<(RewardQueueItem Item, int Position)?> FindNextByLoginAsync(string login)
        {
            if (queueFile.Exists)
            {
                try
                {
                    await successSemaphore.WaitAsync();

                    List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(queueFile.FullName), options);

                    var item = items.FirstOrDefault(x => x.Request.Login.Equals(login, StringComparison.OrdinalIgnoreCase));

                    if (item != null)
                    {
                        return (item, items.IndexOf(item) + 1);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Could not find next queue item for: {login}");
                }
                finally
                {
                    successSemaphore.Release();
                }
            }

            return null;
        }

        public async Task<RewardQueueItem> FindByIndexAsync(int index)
        {
            if (queueFile.Exists)
            {
                try
                {
                    await successSemaphore.WaitAsync();

                    List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(await File.ReadAllTextAsync(queueFile.FullName), options);

                    if (items.Count > 0)
                    {
                        RewardQueueItem item = items.Find(item => items.IndexOf(item) == (index - 1));

                        if (item != null)
                        {
                            return item;
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Could not find next queue item by index: {index}");
                }
                finally
                {
                    successSemaphore.Release();
                }
            }

            return null;
        }

        public void Dispose()
        {
            try
            {
                successSemaphore.Dispose();
            }
            catch
            {

            }

            try
            {
                failedSemaphore.Dispose();
            }
            catch
            {

            }
        }
    }
}
