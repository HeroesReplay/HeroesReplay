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
    private readonly string boardPath;
    private readonly ILogger<RequestQueue> logger;
    private readonly IHeroesProfileService heroesProfileService;
    private readonly AppSettings settings;
    private readonly JsonSerializerOptions options;
    private readonly Mutex queueMutex;
    private readonly Mutex failedMutex;
    private readonly TimeSpan mutexWait;

    public RequestQueue(
        ILogger<RequestQueue> logger,
        IHeroesProfileService heroesProfileService,
        AppSettings settings
    )
        : this(
            logger,
            heroesProfileService,
            settings,
            TimeSpan.FromSeconds(30),
            @"Local\HeroesReplay.RequestQueue",
            @"Local\HeroesReplay.FailedRequests"
        ) { }

    public RequestQueue(
        ILogger<RequestQueue> logger,
        IHeroesProfileService heroesProfileService,
        AppSettings settings,
        TimeSpan mutexWait,
        string queueMutexName,
        string failedMutexName
    )
    {
        this.logger = logger;
        this.heroesProfileService = heroesProfileService;
        this.settings = settings;
        this.mutexWait = mutexWait;
        queueMutex = new Mutex(false, queueMutexName);
        failedMutex = new Mutex(false, failedMutexName);
        queueFile = new FileInfo(
            Path.Combine(settings.Location.DataDirectory, settings.Twitch.QueueFileName)
        );
        failedFile = new FileInfo(
            Path.Combine(settings.Location.DataDirectory, settings.Twitch.FailedFileName)
        );
        boardPath = Path.Combine(settings.Location.DataDirectory, QueueBoard.FileName);
        options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: true) },
        };
        try
        {
            QueueBoard.Write(boardPath, ReadItems(queueFile));
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not write the request queue page.");
        }
    }

    public Task<int> GetItemsInQueue()
    {
        return Task.FromResult(WithLock(queueMutex, () => ReadItems(queueFile).Count, 0));
    }

    public async Task<RewardResponse> EnqueueItemAsync(RewardRequest request)
    {
        try
        {
            if (request.ReplayId.HasValue)
            {
                return await QueueByReplayIdAsync(request).ConfigureAwait(false);
            }

            return await QueueByRewardFilterAsync(request).ConfigureAwait(false);
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

    public Task<RewardQueueItem> DequeueItemAsync()
    {
        return Task.FromResult(
            WithLock(
                queueMutex,
                () =>
                {
                    List<RewardQueueItem> items = ReadItems(queueFile);
                    if (items.Count == 0)
                    {
                        return null;
                    }

                    RewardQueueItem item = items[0];
                    items.RemoveAt(0);
                    SaveQueue(items);
                    logger.LogInformation(
                        "Request: '{Title}' removed from the queue.",
                        item.Request?.RewardTitle
                    );
                    return item;
                },
                null
            )
        );
    }

    public Task<(RewardQueueItem Item, int Position)?> RemoveItemAsync(string login)
    {
        return Task.FromResult(RemoveItem(login));
    }

    public Task<(RewardQueueItem Item, int Position)?> FindNextByLoginAsync(string login)
    {
        return Task.FromResult(
            WithLock(
                queueMutex,
                () =>
                {
                    List<RewardQueueItem> items = ReadItems(queueFile);
                    int index = items.FindIndex(item =>
                        item.Request?.Login != null
                        && item.Request.Login.Equals(login, StringComparison.OrdinalIgnoreCase)
                    );
                    if (index < 0)
                    {
                        return ((RewardQueueItem, int)?)null;
                    }

                    return (items[index], index + 1);
                },
                null
            )
        );
    }

    public Task<RewardQueueItem> FindByIndexAsync(int index)
    {
        return Task.FromResult(
            WithLock(
                queueMutex,
                () =>
                {
                    List<RewardQueueItem> items = ReadItems(queueFile);
                    int offset = index - 1;
                    if (offset < 0 || offset >= items.Count)
                    {
                        return null;
                    }

                    return items[offset];
                },
                null
            )
        );
    }

    public void Dispose()
    {
        queueMutex.Dispose();
        failedMutex.Dispose();
    }

    private async Task<RewardResponse> QueueByReplayIdAsync(RewardRequest request)
    {
        HeroesProfileReplay replay = await heroesProfileService
            .GetReplayByIdAsync(request.ReplayId.Value)
            .ConfigureAwait(false);
        if (replay == null)
        {
            RememberFailure(new RewardQueueItem(request, replay));
            return new RewardResponse(
                success: false,
                message: $"could not find replay with id {request.ReplayId.Value}"
            );
        }

        if (replay.Deleted is > 0 || replay.Downloadable == false)
        {
            RememberFailure(new RewardQueueItem(request, replay));
            return new RewardResponse(
                success: false,
                message: $"the raw file for replay id {request.ReplayId.Value} is no longer available."
            );
        }

        if (
            settings.Spectate?.VersionsSupported != null
            && settings.Spectate.VersionsSupported.Any()
            && !settings.Spectate.VersionsSupported.Contains(replay.GameVersion)
        )
        {
            RememberFailure(new RewardQueueItem(request, replay));
            return new RewardResponse(
                success: false,
                message: $"the version found '{replay.GameVersion}' does not match the supported versions."
            );
        }

        int position = WithLock(
            queueMutex,
            () =>
            {
                List<RewardQueueItem> items = ReadItems(queueFile);
                items.Add(new RewardQueueItem(request, replay));
                SaveQueue(items);
                return items.Count;
            },
            -1
        );
        if (position < 0)
        {
            return new RewardResponse(
                success: false,
                message: "there was an unexpected error with your request."
            );
        }

        return new RewardResponse(
            success: true,
            message: $"{replay.Id} - {ReplayLabel.MapAndRank(replay.Map, replay.Rank)} has been queued. ({position})"
        );
    }

    private async Task<RewardResponse> QueueByRewardFilterAsync(RewardRequest request)
    {
        IEnumerable<HeroesProfileReplay> replays = await heroesProfileService
            .GetReplaysByFilters(request.GameType, request.Rank, request.Map)
            .ConfigureAwait(false);
        HashSet<int> played = PlayedReplayIds.Read(settings.Location?.DataDirectory);
        HeroesProfileReplay chosen = null;
        int position = WithLock(
            queueMutex,
            () =>
            {
                List<RewardQueueItem> items = ReadItems(queueFile);
                var queued = new HashSet<int>();
                foreach (RewardQueueItem item in items)
                {
                    if (item?.HeroesProfileReplay?.Id > 0)
                    {
                        queued.Add(item.HeroesProfileReplay.Id);
                    }
                }

                chosen = RewardCandidateFilter.Choose(
                    replays,
                    played,
                    queued,
                    settings.Spectate?.VersionsSupported
                );
                if (chosen == null)
                {
                    return 0;
                }

                items.Add(new RewardQueueItem(request, chosen));
                SaveQueue(items);
                return items.Count;
            },
            -1
        );
        if (position < 0)
        {
            return new RewardResponse(
                success: false,
                message: "there was an unexpected error with your request."
            );
        }

        if (chosen == null)
        {
            RememberFailure(new RewardQueueItem(request, null));
            return new RewardResponse(
                success: false,
                message: "no recent unplayed replay matched that reward. Replay ids can still be requested directly."
            );
        }

        return new RewardResponse(
            success: true,
            message: $"'{request.RewardTitle}' - {ReplayLabel.MapAndRank(chosen.Map, chosen.Rank)} has been queued ({position})"
        );
    }

    private (RewardQueueItem Item, int Position)? RemoveItem(string login)
    {
        return WithLock(
            queueMutex,
            () =>
            {
                List<RewardQueueItem> items = ReadItems(queueFile);
                int index = items.FindIndex(item =>
                    item.Request?.Login != null
                    && item.Request.Login.Equals(login, StringComparison.OrdinalIgnoreCase)
                );
                if (index < 0)
                {
                    return ((RewardQueueItem, int)?)null;
                }

                RewardQueueItem item = items[index];
                items.RemoveAt(index);
                SaveQueue(items);
                logger.LogInformation(
                    "Request: '{Title}' removed from the queue.",
                    item.Request?.RewardTitle
                );
                return (item, index + 1);
            },
            null
        );
    }

    private void RememberFailure(RewardQueueItem item)
    {
        WithLock(
            failedMutex,
            () =>
            {
                List<RewardQueueItem> items = ReadItems(failedFile);
                items.Add(item);
                WriteItems(failedFile, items);
                return 0;
            },
            0
        );
    }

    private void SaveQueue(List<RewardQueueItem> items)
    {
        WriteItems(queueFile, items);
        QueueBoard.Write(boardPath, items);
    }

    private List<RewardQueueItem> ReadItems(FileInfo file)
    {
        if (file == null || !file.Exists)
        {
            return new List<RewardQueueItem>();
        }

        try
        {
            List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(
                File.ReadAllText(file.FullName),
                options
            );
            return items ?? new List<RewardQueueItem>();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not read {QueueFile}.", file.Name);
            return new List<RewardQueueItem>();
        }
    }

    private void WriteItems(FileInfo file, List<RewardQueueItem> items)
    {
        Directory.CreateDirectory(file.DirectoryName);
        File.WriteAllText(file.FullName, JsonSerializer.Serialize(items, options));
        file.Refresh();
    }

    private T WithLock<T>(Mutex mutex, Func<T> work, T busy)
    {
        return Task
            .Factory.StartNew(
                () =>
                {
                    bool owned = false;
                    try
                    {
                        try
                        {
                            owned = mutex.WaitOne(mutexWait);
                        }
                        catch (AbandonedMutexException)
                        {
                            owned = true;
                        }

                        if (!owned)
                        {
                            logger.LogWarning("Request queue is busy. Skipping this pass.");
                            return busy;
                        }

                        return work();
                    }
                    finally
                    {
                        if (owned)
                        {
                            try
                            {
                                mutex.ReleaseMutex();
                            }
                            catch (ApplicationException) { }
                        }
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            )
            .GetAwaiter()
            .GetResult();
    }
}
