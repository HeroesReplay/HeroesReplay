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

namespace HeroesReplay.Core.Services.Queue;

public class RequestQueue : IRequestQueue, IDisposable
{
    private readonly FileInfo queueFile;
    private readonly FileInfo failedFile;
    private readonly ILogger<RequestQueue> logger;
    private readonly IHeroesProfileService heroesProfileService;
    private readonly AppSettings settings;
    private readonly JsonSerializerOptions options;
    private readonly Mutex queueMutex = new(false, @"Local\HeroesReplay.RequestQueue");
    private readonly Mutex failedMutex = new(false, @"Local\HeroesReplay.FailedRequests");

    public RequestQueue(
        ILogger<RequestQueue> logger,
        IHeroesProfileService heroesProfileService,
        AppSettings settings
    )
    {
        this.logger = logger;
        this.heroesProfileService = heroesProfileService;
        this.settings = settings;

        queueFile = new(
            Path.Combine(settings.Location.DataDirectory, settings.Twitch.QueueFileName)
        );
        failedFile = new(
            Path.Combine(settings.Location.DataDirectory, settings.Twitch.FailedFileName)
        );

        options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: true) },
        };
    }

    private async Task<IDisposable> AcquireAsync(Mutex mutex)
    {
        bool acquired = await Task.Run(() =>
            {
                try
                {
                    return mutex.WaitOne(TimeSpan.FromSeconds(30));
                }
                catch (AbandonedMutexException)
                {
                    return true;
                }
            })
            .ConfigureAwait(false);
        if (!acquired)
        {
            throw new TimeoutException("Could not lock a request queue file.");
        }

        return new MutexReleaser(mutex);
    }

    private sealed class MutexReleaser : IDisposable
    {
        private Mutex mutex;

        public MutexReleaser(Mutex mutex)
        {
            this.mutex = mutex;
        }

        public void Dispose()
        {
            if (mutex == null)
            {
                return;
            }

            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException) { }

            mutex = null;
        }
    }

    public async Task<int> GetItemsInQueue()
    {
        if (!queueFile.Exists)
        {
            return 0;
        }

        using (await AcquireAsync(queueMutex).ConfigureAwait(false))
        {
            List<RewardQueueItem> requests = JsonSerializer.Deserialize<List<RewardQueueItem>>(
                await File.ReadAllTextAsync(queueFile.FullName),
                options
            );
            return requests?.Count ?? 0;
        }
    }

    public async Task<RewardResponse> EnqueueItemAsync(RewardRequest request)
    {
        try
        {
            using (await AcquireAsync(queueMutex).ConfigureAwait(false))
            {
                if (request.ReplayId.HasValue)
                {
                    return await QueueByReplayIdAsync(request);
                }

                return await QueueByRewardFilterAsync(request);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not queue request");
            return new RewardResponse(
                success: false,
                message: "there was an unexpected error with your request."
            );
        }
    }

    private async Task<RewardResponse> QueueByReplayIdAsync(RewardRequest request)
    {
        HeroesProfileReplay replay = await heroesProfileService.GetReplayByIdAsync(
            request.ReplayId.Value
        );

        if (replay == null)
        {
            await AddToFailedRequestsAsync(new(request, replay));
            return new RewardResponse(
                success: false,
                message: $"could not find replay with id {request.ReplayId.Value}"
            );
        }

        if (replay.Deleted is > 0)
        {
            await AddToFailedRequestsAsync(new(request, replay));
            return new RewardResponse(
                success: false,
                message: $"the raw file for replay id {request.ReplayId.Value} is no longer available."
            );
        }

        if (!settings.Spectate.VersionsSupported.Contains(replay.GameVersion))
        {
            await AddToFailedRequestsAsync(new(request, replay));
            return new RewardResponse(
                success: false,
                message: $"the version found '{replay.GameVersion}' does not match the supported versions."
            );
        }

        int position = await QueueReplayId(new(request, replay));
        return new RewardResponse(
            success: true,
            message: $"{replay.Id} - {replay.Map} ({replay.Rank}) has been queued. ({position})"
        );
    }

    private async Task<int> QueueReplayId(RewardQueueItem item)
    {
        if (!queueFile.Exists)
        {
            await File.WriteAllTextAsync(
                queueFile.FullName,
                JsonSerializer.Serialize(new List<RewardQueueItem> { item }, options)
            );
            return 1;
        }
        else
        {
            List<RewardQueueItem> items = new(
                JsonSerializer.Deserialize<List<RewardQueueItem>>(
                    await File.ReadAllTextAsync(queueFile.FullName),
                    options
                )
            )
            {
                item,
            };
            await File.WriteAllTextAsync(
                queueFile.FullName,
                JsonSerializer.Serialize(items, options)
            );
            return items.Count;
        }
    }

    private async Task AddToFailedRequestsAsync(RewardQueueItem item)
    {
        using (await AcquireAsync(failedMutex).ConfigureAwait(false))
        {
            if (failedFile.Exists)
            {
                List<RewardQueueItem> items = new(
                    JsonSerializer.Deserialize<List<RewardQueueItem>>(
                        await File.ReadAllTextAsync(failedFile.FullName),
                        options
                    )
                )
                {
                    item,
                };
                await File.WriteAllTextAsync(
                    failedFile.FullName,
                    JsonSerializer.Serialize(items, options)
                );
            }
            else
            {
                await File.WriteAllTextAsync(
                    failedFile.FullName,
                    JsonSerializer.Serialize(new List<RewardQueueItem> { item }, options)
                );
            }
        }
    }

    private async Task<RewardResponse> QueueByRewardFilterAsync(RewardRequest request)
    {
        IEnumerable<HeroesProfileReplay> replays = await heroesProfileService.GetReplaysByFilters(
            request.GameType,
            request.Rank,
            request.Map
        );
        HeroesProfileReplay replay = replays.OrderBy(x => Guid.NewGuid()).FirstOrDefault();

        if (replay != null)
        {
            int position = await QueueReplayId(new(request, replay));
            return new RewardResponse(
                success: true,
                message: $"'{request.RewardTitle}' - {replay.Map} ({replay.Rank}) has been queued ({position})"
            );
        }
        else
        {
            await AddToFailedRequestsAsync(new(request, replay));
            return new RewardResponse(
                success: false,
                message: "Request failed to queue because the given reward criteria could not be found"
            );
        }
    }

    public async Task<RewardQueueItem> DequeueItemAsync()
    {
        if (queueFile.Exists)
        {
            try
            {
                using (await AcquireAsync(queueMutex).ConfigureAwait(false))
                {
                    List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(
                        await File.ReadAllTextAsync(queueFile.FullName),
                        options
                    );

                    if (items.Count > 0)
                    {
                        RewardQueueItem item = items[0];

                        if (items.Remove(item))
                        {
                            await File.WriteAllTextAsync(
                                queueFile.FullName,
                                JsonSerializer.Serialize(items, options)
                            );
                            logger.LogInformation(
                                $"Request: '{item.Request.RewardTitle}' removed from the queue."
                            );
                            return item;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not dequeue item");
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
                using (await AcquireAsync(queueMutex).ConfigureAwait(false))
                {
                    List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(
                        await File.ReadAllTextAsync(queueFile.FullName),
                        options
                    );

                    if (items.Count > 0)
                    {
                        RewardQueueItem item = items.Find(item =>
                            item.Request.Login.Equals(login, StringComparison.OrdinalIgnoreCase)
                        );

                        if (item != null)
                        {
                            int position = items.IndexOf(item) + 1;

                            if (items.Remove(item))
                            {
                                await File.WriteAllTextAsync(
                                    queueFile.FullName,
                                    JsonSerializer.Serialize(items, options)
                                );
                                logger.LogInformation(
                                    $"Request: '{item.Request.RewardTitle}' removed from the queue."
                                );
                                return (item, position);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not remove item from queue");
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
                using (await AcquireAsync(queueMutex).ConfigureAwait(false))
                {
                    List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(
                        await File.ReadAllTextAsync(queueFile.FullName),
                        options
                    );

                    var item = items.FirstOrDefault(x =>
                        x.Request.Login.Equals(login, StringComparison.OrdinalIgnoreCase)
                    );

                    if (item != null)
                    {
                        return (item, items.IndexOf(item) + 1);
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not find next queue item for: {login}");
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
                using (await AcquireAsync(queueMutex).ConfigureAwait(false))
                {
                    List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(
                        await File.ReadAllTextAsync(queueFile.FullName),
                        options
                    );

                    if (items.Count > 0)
                    {
                        RewardQueueItem item = items.Find(item =>
                            items.IndexOf(item) == (index - 1)
                        );

                        if (item != null)
                        {
                            return item;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not find next queue item by index: {index}");
            }
        }

        return null;
    }

    public void Dispose()
    {
        queueMutex.Dispose();
        failedMutex.Dispose();
    }
}
