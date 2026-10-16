using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Twitch.Predictions;
using HeroesReplay.Core.Twitch.Rewards;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Requests;

public class RequestQueue : IRequestQueue, IDisposable
{
    private readonly FileInfo queueFile;
    private readonly FileInfo failedFile;
    private readonly string boardPath;
    private readonly ILogger<RequestQueue> logger;
    private readonly IHeroesProfileService heroesProfileService;
    private readonly IReplayTierLookup tierLookup;
    private readonly AppSettings settings;
    private readonly ICustomRewardsHolder rewardsHolder;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) },
    };
    private readonly Mutex queueMutex;
    private readonly Mutex failedMutex;
    private readonly TimeSpan mutexWait;
    private const int DuplicateQueuePosition = -2;

    /// <summary>How long a request given up after it was queued stays on the queue page.</summary>
    public static readonly TimeSpan FailureWindow = TimeSpan.FromHours(24);

    public RequestQueue(
        ILogger<RequestQueue> logger,
        IHeroesProfileService heroesProfileService,
        AppSettings settings,
        ICustomRewardsHolder rewardsHolder,
        IReplayTierLookup tierLookup
    )
        : this(
            logger,
            heroesProfileService,
            settings,
            TimeSpan.FromSeconds(30),
            @"Local\HeroesReplay.RequestQueue",
            @"Local\HeroesReplay.FailedRequests",
            rewardsHolder,
            tierLookup
        ) { }

    public RequestQueue(
        ILogger<RequestQueue> logger,
        IHeroesProfileService heroesProfileService,
        AppSettings settings,
        TimeSpan mutexWait,
        string queueMutexName,
        string failedMutexName,
        ICustomRewardsHolder rewardsHolder = null,
        IReplayTierLookup tierLookup = null
    )
    {
        this.logger = logger;
        this.heroesProfileService = heroesProfileService;
        this.tierLookup = tierLookup;
        this.settings = settings;
        this.rewardsHolder = rewardsHolder;
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
        try
        {
            QueueBoard.Write(
                boardPath,
                ReadItems(queueFile),
                Rewards(),
                Failures(),
                RatingPrediction.WindowFor(settings)
            );
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not write the request queue page.");
        }
    }

    /// <summary>
    /// The items in the queue file at <paramref name="path"/>, read without the queue lock and
    /// without moving an unreadable file aside. Empty when there is no file; null when it
    /// cannot be read or parsed.
    /// </summary>
    public static IReadOnlyList<RewardQueueItem> Snapshot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new List<RewardQueueItem>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<RewardQueueItem>>(
                    File.ReadAllText(path),
                    Options
                ) ?? new List<RewardQueueItem>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
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
            return Unqueued(
                request,
                null,
                "there was an unexpected error with your request.",
                remember: false
            );
        }
    }

    public Task<RewardQueueItem> PeekDownloadAsync(DateTimeOffset now)
    {
        return Task.FromResult(
            WithLock(
                queueMutex,
                () =>
                    ReadItems(queueFile)
                        .Find(item =>
                            item != null
                            && (
                                item.Download?.NextAttemptAt is not DateTimeOffset next
                                || next <= now
                            )
                        ),
                null
            )
        );
    }

    public Task<RequestCompletion> CompleteDownloadAsync(RewardQueueItem item, Action publish)
    {
        if (item == null)
        {
            return Task.FromResult(RequestCompletion.NotQueued);
        }

        return Task.FromResult(
            WithLock(
                queueMutex,
                () =>
                {
                    List<RewardQueueItem> items = ReadItems(queueFile);
                    int index = items.FindIndex(queued =>
                        queued != null && queued.IsSameRequest(item)
                    );
                    if (index < 0)
                    {
                        return RequestCompletion.NotQueued;
                    }

                    // The replay reaches the disk before the request leaves the queue. A kill
                    // between the two leaves the request queued with its replay already there,
                    // and the next pass finds the file and only dequeues (#351).
                    publish?.Invoke();
                    items.RemoveAt(index);
                    SaveQueue(items);
                    logger.LogInformation(
                        "Request: '{Title}' removed from the queue. Its replay is on disk.",
                        item.Request?.RewardTitle
                    );
                    return RequestCompletion.Completed;
                },
                RequestCompletion.Busy
            )
        );
    }

    public Task<RequestDownload> RetryDownloadLaterAsync(
        RewardQueueItem item,
        string error,
        DateTimeOffset now
    )
    {
        if (item == null)
        {
            return Task.FromResult<RequestDownload>(null);
        }

        return Task.FromResult(
            WithLock(
                queueMutex,
                () =>
                {
                    List<RewardQueueItem> items = ReadItems(queueFile);
                    RewardQueueItem queued = items.Find(entry =>
                        entry != null && entry.IsSameRequest(item)
                    );
                    if (queued == null)
                    {
                        return null;
                    }

                    RequestDownload download = queued.Download ?? new RequestDownload();
                    download.Attempts++;
                    download.LastError = error;
                    download.NextAttemptAt = now + RequestDownloadRetry.Delay(download.Attempts);
                    queued.Download = download;
                    SaveQueue(items);
                    return download;
                },
                null
            )
        );
    }

    public Task<bool> FailDownloadAsync(
        RewardQueueItem item,
        string reason,
        bool refundRequested,
        DateTimeOffset now
    )
    {
        if (item == null)
        {
            return Task.FromResult(true);
        }

        RequestDownload download = item.Download ?? new RequestDownload();
        download.NextAttemptAt = null;
        download.FailedAt = now;
        download.FailureReason = reason;
        download.RefundRequested = refundRequested;
        item.Download = download;

        // Kept as failed before it leaves the queue: a kill in between leaves it in both, and
        // the next pass replaces the failed record instead of adding a second one.
        RememberDownloadFailure(item);
        return Task.FromResult(
            WithLock(
                queueMutex,
                () =>
                {
                    List<RewardQueueItem> items = ReadItems(queueFile);
                    int removed = items.RemoveAll(queued =>
                        queued != null && queued.IsSameRequest(item)
                    );
                    if (removed > 0)
                    {
                        SaveQueue(items);
                        logger.LogInformation(
                            "Request: '{Title}' removed from the queue. It could not be downloaded.",
                            item.Request?.RewardTitle
                        );
                    }

                    return true;
                },
                false
            )
        );
    }

    /// <summary>
    /// Requests given up in the last <paramref name="window"/>, newest first, at most
    /// <paramref name="limit"/>: the queue page's "Could not play" list (#351). Records from
    /// before #351 have no reason and are left out.
    /// </summary>
    public static IReadOnlyList<RewardQueueItem> RecentFailures(
        IEnumerable<RewardQueueItem> failed,
        DateTimeOffset now,
        TimeSpan? window = null,
        int limit = 5
    )
    {
        if (failed == null)
        {
            return new List<RewardQueueItem>();
        }

        DateTimeOffset since = now - (window ?? FailureWindow);
        return failed
            .Where(item =>
                item?.Download?.FailedAt is DateTimeOffset at
                && at >= since
                && !string.IsNullOrWhiteSpace(item.Download.FailureReason)
            )
            .OrderByDescending(item => item.Download.FailedAt)
            .Take(limit)
            .ToList();
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
            return Unqueued(
                request,
                null,
                $"could not find replay with id {request.ReplayId.Value}."
            );
        }

        if (replay.Deleted is > 0 || replay.Downloadable == false)
        {
            return Unqueued(
                request,
                replay,
                $"the raw file for replay id {request.ReplayId.Value} is no longer available."
            );
        }

        if (
            !GameVersionOrder.Allows(
                replay.GameVersion,
                settings.Spectate?.VersionsSupported,
                settings.Spectate?.MinimumGameVersion
            )
        )
        {
            string floor = settings.Spectate?.MinimumGameVersion;
            string required = string.IsNullOrWhiteSpace(floor)
                ? "a supported client"
                : $"{floor} or newer";
            return Unqueued(
                request,
                replay,
                $"replay {request.ReplayId.Value} is client {replay.GameVersion}. We only play {required}."
            );
        }

        int position = WithLock(
            queueMutex,
            () =>
            {
                List<RewardQueueItem> items = ReadItems(queueFile);
                if (IsDuplicateRedemption(request, items))
                {
                    return DuplicateQueuePosition;
                }

                items.Add(new RewardQueueItem(request, replay));
                SaveQueue(items);
                return items.Count;
            },
            -1
        );
        if (position == DuplicateQueuePosition)
        {
            return DuplicateResponse(request);
        }

        if (position < 0)
        {
            return Unqueued(
                request,
                null,
                "there was an unexpected error with your request.",
                remember: false
            );
        }

        return new RewardResponse(success: true, message: QueueMessage(request, replay, position));
    }

    private static string QueueMessage(
        RewardRequest request,
        HeroesProfileReplay replay,
        int position
    )
    {
        string focus = string.IsNullOrWhiteSpace(request?.BattleTag)
            ? string.Empty
            : $" Focus follows {request.BattleTag} while they are alive.";
        return $"{replay.Id} - {ReplayLabel.MapAndRank(replay.Map, replay.Rank)} has been queued. ({position}){focus}";
    }

    private async Task<RewardResponse> QueueByRewardFilterAsync(RewardRequest request)
    {
        if (request.RankRequired && request.Rank == null)
        {
            return Unqueued(request, null, $"that is not a rank. Enter {RankInput.Names}.");
        }

        IEnumerable<HeroesProfileReplay> replays = await heroesProfileService
            .GetReplaysByFilters(request.GameType, request.Rank, request.Map)
            .ConfigureAwait(false);
        HashSet<int> played = PlayedReplayIds.Read(settings.Location?.DataDirectory);
        string tier = null;
        if (request.Rank is GameRank rank)
        {
            // A repeated delivery of a queued redemption is not refunded, so it must not reach
            // the search: a search that finds nothing records a refund.
            if (
                WithLock(
                    queueMutex,
                    () => IsDuplicateRedemption(request, ReadItems(queueFile)),
                    false
                )
            )
            {
                return DuplicateResponse(request);
            }

            RankedPick pick = await PickAtRankAsync(request, rank, replays, played)
                .ConfigureAwait(false);
            if (pick.Replay == null && pick.Looked > 0)
            {
                return Unqueued(
                    request,
                    null,
                    pick.TimedOut
                        ? $"Heroes Profile took too long to check the ranks of recent games on {request.Map}."
                        : $"I couldn't find a recent {rank} game on {request.Map}. None of the {pick.Looked} recent games I checked there was {rank}.",
                    tip: "Try another rank, or redeem ReplayId and paste a Heroes Profile replay number."
                );
            }

            tier = pick.Tier;
            replays =
                pick.Replay == null
                    ? Enumerable.Empty<HeroesProfileReplay>()
                    : new[] { pick.Replay };
        }

        HeroesProfileReplay chosen = null;
        int position = WithLock(
            queueMutex,
            () =>
            {
                List<RewardQueueItem> items = ReadItems(queueFile);
                if (IsDuplicateRedemption(request, items))
                {
                    return DuplicateQueuePosition;
                }

                chosen = RewardCandidateFilter.Choose(
                    replays,
                    played,
                    QueuedIds(items),
                    settings.Spectate?.VersionsSupported,
                    settings.Spectate?.MinimumGameVersion
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
        if (position == DuplicateQueuePosition)
        {
            return DuplicateResponse(request);
        }

        if (position < 0)
        {
            return Unqueued(
                request,
                null,
                "there was an unexpected error with your request.",
                remember: false
            );
        }

        if (chosen == null)
        {
            return Unqueued(
                request,
                null,
                "I couldn't queue a game for that reward. Every recent match for it was already played, already waiting, or from an older patch.",
                tip: "Redeem ReplayId and paste a Heroes Profile replay number to request one specific game."
            );
        }

        string shown = ReplayRequestKind.Blind(request)
            ? ReplayRequestKind.HiddenRank
            : tier ?? chosen.Rank;
        return new RewardResponse(
            success: true,
            message: $"'{request.RewardTitle}' - {ReplayLabel.MapAndRank(chosen.Map, shown)} has been queued ({position})"
        );
    }

    /// <summary>
    /// A rank reward's game (#429): the recent games of its type and map that are not played,
    /// queued or below the patch line, looked up until one is in the entered league. A Storm
    /// League game keeps that tier as its rank, so its badge needs no second lookup. Other modes
    /// show no rank badge, so their rank stays unset.
    /// </summary>
    private async Task<RankedPick> PickAtRankAsync(
        RewardRequest request,
        GameRank rank,
        IEnumerable<HeroesProfileReplay> replays,
        ISet<int> played
    )
    {
        if (tierLookup == null || request.GameType is not GameType gameType)
        {
            logger.LogWarning(
                "'{Title}' cannot check ranks: {Missing}.",
                request.RewardTitle,
                tierLookup == null ? "no tier lookup" : "no game type"
            );
            return new RankedPick(null, null, Looked: 0, TimedOut: false);
        }

        HashSet<int> queued = WithLock(
            queueMutex,
            () => QueuedIds(ReadItems(queueFile)),
            new HashSet<int>()
        );
        List<HeroesProfileReplay> candidates = RewardCandidateFilter.Eligible(
            replays,
            played,
            queued,
            settings.Spectate?.VersionsSupported,
            settings.Spectate?.MinimumGameVersion
        );
        HeroesProfileApiSettings api = settings.HeroesProfileApi ?? new HeroesProfileApiSettings();
        using var timeout = new CancellationTokenSource(
            api.RankSearchTimeout > TimeSpan.Zero ? api.RankSearchTimeout : Timeout.InfiniteTimeSpan
        );
        RankedPick pick = await RankedCandidatePick
            .FindAsync(
                candidates,
                rank,
                (replay, token) => tierLookup.GetTierAsync(replay.Id, gameType, token),
                api.RankLookupLimit,
                Random.Shared,
                timeout.Token
            )
            .ConfigureAwait(false);
        if (pick.Replay == null)
        {
            logger.LogInformation(
                "'{Title}': no {Rank} game among {Looked} of {Candidates} recent {GameType} games on {Map}{TimedOut}.",
                request.RewardTitle,
                rank,
                pick.Looked,
                candidates.Count,
                gameType,
                request.Map,
                pick.TimedOut ? " (timed out)" : string.Empty
            );
            return pick;
        }

        logger.LogInformation(
            "'{Title}': replay {ReplayId} is {Tier} ({Looked} of {Candidates} looked up).",
            request.RewardTitle,
            pick.Replay.Id,
            pick.Tier,
            pick.Looked,
            candidates.Count
        );
        if (gameType == GameType.StormLeague)
        {
            pick.Replay.Rank = pick.Tier;
        }

        return pick;
    }

    private static HashSet<int> QueuedIds(IEnumerable<RewardQueueItem> items)
    {
        var queued = new HashSet<int>();
        foreach (RewardQueueItem item in items ?? Enumerable.Empty<RewardQueueItem>())
        {
            if (item?.HeroesProfileReplay?.Id > 0)
            {
                queued.Add(item.HeroesProfileReplay.Id);
            }
        }

        return queued;
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

    /// <summary>
    /// A redemption that was not queued (#425): it goes to the failed list (unless
    /// <paramref name="remember"/> is false) and gets its points back. Like the download role,
    /// the queue does not call Twitch: it records a cancel in
    /// <c>Data\redemption-dispositions.txt</c>, and <c>twitch connect</c> sends it
    /// (<see cref="RedemptionFulfiller"/>). Before #425 these redemptions stayed UNFULFILLED,
    /// so the viewer's points were spent. A request with no redemption has no points to return.
    /// </summary>
    private RewardResponse Unqueued(
        RewardRequest request,
        HeroesProfileReplay replay,
        string reason,
        string tip = null,
        bool remember = true
    )
    {
        if (remember)
        {
            RememberFailure(new RewardQueueItem(request, replay));
        }

        bool refund = request?.RedemptionId is Guid id && id != Guid.Empty;
        if (refund)
        {
            RedemptionDispositionLog.Append(
                Path.Combine(settings.Location.DataDirectory, RedemptionDispositionLog.FileName),
                replay?.Id ?? request.ReplayId,
                request,
                RedemptionEnd.Cancel
            );
        }

        string message = reason;
        if (refund)
        {
            message += " Your points will be returned.";
        }

        if (!string.IsNullOrWhiteSpace(tip))
        {
            message += " " + tip;
        }

        return new RewardResponse(success: false, message: message);
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

    /// <summary>
    /// A request given up after it was queued (#351). Its earlier record, from a pass that was
    /// killed before the request left the queue, is replaced.
    /// </summary>
    private void RememberDownloadFailure(RewardQueueItem item)
    {
        WithLock(
            failedMutex,
            () =>
            {
                List<RewardQueueItem> items = ReadItems(failedFile);
                items.RemoveAll(failed =>
                    failed?.Download?.FailedAt != null && failed.IsSameRequest(item)
                );
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
        QueueBoard.Write(
            boardPath,
            items,
            Rewards(),
            Failures(),
            RatingPrediction.WindowFor(settings)
        );
    }

    /// <summary>The queue page's recent failures. The failed file is read under its own lock.</summary>
    private IReadOnlyList<RewardQueueItem> Failures()
    {
        return WithLock(
            failedMutex,
            () => RecentFailures(ReadItems(failedFile), DateTimeOffset.UtcNow),
            null
        );
    }

    private IReadOnlyList<SupportedReward> Rewards()
    {
        try
        {
            return rewardsHolder?.Rewards;
        }
        catch (Exception e)
        {
            // Rewards come from the map catalog, which may not be loaded yet.
            logger.LogDebug(e, "Could not read the channel-point rewards for the queue page.");
            return null;
        }
    }

    private List<RewardQueueItem> ReadItems(FileInfo file)
    {
        if (file == null || !file.Exists)
        {
            return new List<RewardQueueItem>();
        }

        try
        {
            string json = DurableFile.ReadOrAside(file.FullName);
            if (json == null)
            {
                if (File.Exists(file.FullName))
                {
                    logger.LogError("Could not read {QueueFile}. It was left in place.", file.Name);
                    return new List<RewardQueueItem>();
                }

                logger.LogError(
                    "Could not read {QueueFile}. The unreadable copy was moved aside.",
                    file.Name
                );
                file.Refresh();
                return new List<RewardQueueItem>();
            }

            List<RewardQueueItem> items = JsonSerializer.Deserialize<List<RewardQueueItem>>(
                json,
                Options
            );
            return items ?? new List<RewardQueueItem>();
        }
        catch (Exception e)
        {
            DurableFile.Aside(file.FullName);
            logger.LogError(
                e,
                "Could not read {QueueFile}. The unreadable copy was moved aside.",
                file.Name
            );
            file.Refresh();
            return new List<RewardQueueItem>();
        }
    }

    private void WriteItems(FileInfo file, List<RewardQueueItem> items)
    {
        DurableFile.Replace(file.FullName, JsonSerializer.Serialize(items, Options));
        file.Refresh();
    }

    private static bool IsDuplicateRedemption(RewardRequest request, List<RewardQueueItem> items)
    {
        if (request == null || request.RedemptionId == Guid.Empty || items == null)
        {
            return false;
        }

        foreach (RewardQueueItem item in items)
        {
            if (item?.Request != null && item.Request.RedemptionId == request.RedemptionId)
            {
                return true;
            }
        }

        return false;
    }

    private RewardResponse DuplicateResponse(RewardRequest request)
    {
        logger.LogInformation(
            "Redemption {RedemptionId} for '{Title}' is already queued.",
            request.RedemptionId,
            request.RewardTitle
        );
        return new RewardResponse(
            success: false,
            message: $"'{request.RewardTitle}' is already queued.",
            duplicate: true
        );
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
