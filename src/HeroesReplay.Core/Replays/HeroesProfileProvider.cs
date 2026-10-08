using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Telemetry;
using HeroesReplay.Core.Twitch.Rewards;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;

namespace HeroesReplay.Core.Replays;

public class HeroesProfileProvider : IReplayProvider
{
    private readonly AppSettings settings;
    private readonly CancellationTokenProvider provider;
    private readonly ILogger<HeroesProfileProvider> logger;
    private readonly IReplayLoader replayLoader;
    private readonly IReplayHelper replayHelper;
    private readonly IHeroesProfileService heroesProfileService;
    private readonly IRequestQueue requestQueue;
    private readonly IHeroesProfileResume heroesProfileResume;
    private readonly ReplayListBackoff listBackoff;
    private LoadedReplay staged;
    private int? heldBackId;
    private int minReplayId;
    private Func<IReadOnlyList<string>> installedVersionSource;
    private Func<string, Replay> headerSource = ReplayHeader.Load;
    private Func<DateTimeOffset> clock = () => DateTimeOffset.UtcNow;
    private readonly Dictionary<int, ReplayMediaPolicyInput> mediaFacts = new();

    public bool ContinuesWhenEmpty => true;

    private int MinReplayId
    {
        get
        {
            if (minReplayId == default)
            {
                FileInfo latest = StandardDirectory
                    .GetFiles(settings.StormReplay.WildCard)
                    .Select(file =>
                    {
                        bool parsed = replayHelper.TryGetReplayId(file.Name, out int id);
                        return (file, parsed, id);
                    })
                    .Where(item => item.parsed)
                    .OrderByDescending(item => item.id)
                    .Select(item => item.file)
                    .FirstOrDefault();

                if (latest != null && replayHelper.TryGetReplayId(latest.Name, out int replayId))
                {
                    MinReplayId = replayId;
                }
                else
                {
                    MinReplayId = settings.HeroesProfileApi.MinReplayId;
                }
            }

            return minReplayId;
        }
        set => minReplayId = value;
    }

    private DirectoryInfo StandardDirectory
    {
        get
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(settings.StandardReplayCachePath);
            if (!directoryInfo.Exists)
                directoryInfo.Create();
            return directoryInfo;
        }
    }

    private DirectoryInfo RequestsDirectory
    {
        get
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(settings.RequestedReplayCachePath);
            if (!directoryInfo.Exists)
                directoryInfo.Create();
            return directoryInfo;
        }
    }

    public HeroesProfileProvider(
        ILogger<HeroesProfileProvider> logger,
        IReplayLoader replayLoader,
        IReplayHelper replayHelper,
        IRequestQueue requestQueue,
        IHeroesProfileService heroesProfileService,
        CancellationTokenProvider provider,
        AppSettings settings,
        IHeroesProfileResume heroesProfileResume = null
    )
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.replayLoader = replayLoader ?? throw new ArgumentNullException(nameof(replayLoader));
        this.replayHelper = replayHelper ?? throw new ArgumentNullException(nameof(replayHelper));
        this.requestQueue = requestQueue ?? throw new ArgumentNullException(nameof(requestQueue));
        this.heroesProfileService =
            heroesProfileService ?? throw new ArgumentNullException(nameof(heroesProfileService));
        this.heroesProfileResume = heroesProfileResume;
        listBackoff = new ReplayListBackoff(
            (settings.ServiceHealth ?? new ServiceHealthSettings()).NextDependencyProbe(
                failed: true
            ),
            logger
        );
    }

    /// <summary>
    /// The download role's dependency probe (#305): a rejected Heroes Profile key pauses the
    /// replay list until a probe passes (#358).
    /// </summary>
    public void ObserveDependency(ServiceDependencyResult result) =>
        listBackoff.Observe(result, clock());

    internal void UseInstalledVersions(Func<IReadOnlyList<string>> source)
    {
        installedVersionSource = source;
    }

    internal void UseClock(Func<DateTimeOffset> source)
    {
        clock = source ?? (() => DateTimeOffset.UtcNow);
    }

    internal void UseReplayHeaders(Func<string, Replay> source)
    {
        headerSource = source ?? ReplayHeader.Load;
    }

    /// <summary>
    /// Download the next Storm League replay or a queued reward replay.
    /// Does not load or spectate the file. Used by the downloader process.
    /// </summary>
    public async Task<bool> DownloadNextAsync()
    {
        MediaRetention.SweepAndLog(settings, logger);
        if (settings.Twitch.EnableRequests)
        {
            RequestFetch fetched = await FetchRequestAsync().ConfigureAwait(false);
            if (fetched.Taken)
            {
                // A request whose download Heroes Profile did not answer still counts toward
                // the role's outage mode. It stays queued either way (#351).
                fetched.Outage?.Throw();
                return fetched.File != null;
            }
        }

        WaitingReplays waiting = UnspectatedOnDisk();
        if (waiting.Fresh >= CachedReplayLimit)
        {
            logger.LogInformation(
                "{Waiting} replays waiting to be spectated ({Fresh} fresh, limit {Limit}). Not downloading another.",
                waiting.Waiting,
                waiting.Fresh,
                CachedReplayLimit
            );
            return false;
        }

        HeroesProfileReplay replay = await GetNextReplayAsync().ConfigureAwait(false);
        if (replay == null)
        {
            return false;
        }

        await heroesProfileService.EnrichRankAsync(replay, provider.Token).ConfigureAwait(false);
        // A replay already past its window would never count toward the limit, so it keeps the
        // old cap on every waiting replay. Otherwise expired downloads would never stop.
        if (waiting.Waiting >= CachedReplayLimit && IsPastMediaWindow(replay))
        {
            logger.LogInformation(
                "Replay {ReplayId} is past its media window and {Waiting} replays are waiting (limit {Limit}). Not downloading it.",
                replay.Id,
                waiting.Waiting,
                CachedReplayLimit
            );
            return false;
        }

        FileInfo fileInfo = GetFileInfo(StandardDirectory, replay);
        if (!fileInfo.Exists)
        {
            try
            {
                await DownloadReplayAsync(replay, fileInfo).ConfigureAwait(false);
            }
            // Heroes Profile answered (after the HTTP pipeline's retries), so this is not an
            // outage. The listing cursor is already past this replay, so the next call lists the
            // one after it (#346).
            catch (ApiException e) when (e.ResponseStatusCode > 0)
            {
                logger.LogWarning(
                    "Skipped replay {ReplayId}: Heroes Profile answered its download with HTTP {Status}. The listing continues after it.",
                    replay.Id,
                    e.ResponseStatusCode
                );
                return false;
            }
        }

        return true;
    }

    private bool IsPastMediaWindow(HeroesProfileReplay replay) =>
        ReplayMediaPolicy.IsPastWindow(
            ReplayMediaFacts.From(
                new LoadedReplay { ReplayId = replay.Id, HeroesProfileReplay = replay },
                alreadyPublished: false,
                alreadyScheduled: false,
                inOutbox: false
            ),
            settings.ReplayMedia,
            DateTime.UtcNow
        );

    public void Requeue(LoadedReplay replay)
    {
        if (replay == null)
        {
            return;
        }

        staged = replay;
        logger.LogInformation(
            "Returned replay {ReplayId} to the front of the Heroes Profile queue.",
            replay.ReplayId
        );
    }

    public void Defer(LoadedReplay replay)
    {
        if (replay == null)
        {
            return;
        }

        if (staged?.ReplayId == replay.ReplayId)
        {
            staged = null;
        }

        logger.LogInformation(
            "Replay {ReplayId} leaves the front of the Heroes Profile queue. It is not marked spectated.",
            replay.ReplayId
        );
    }

    public void MarkSpectated(LoadedReplay replay) { }

    public void HoldBack(int replayId)
    {
        if (replayId > 0)
        {
            heldBackId = replayId;
        }
    }

    public async Task<LoadedReplay> TryLoadNextReplayAsync()
    {
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.replay.load");
        if (staged != null && staged.ReplayId != heldBackId)
        {
            LoadedReplay ready = staged;
            staged = null;
            TagLoaded(activity, ready);
            return ready;
        }

        LoadedReplay loaded;
        if (settings.Twitch.EnableRequests)
        {
            RequestFetch fetched = await TryFetchRequestAsync().ConfigureAwait(false);
            if (fetched.Taken)
            {
                activity?.SetTag("replay.source", "request");
                loaded = await LoadRequestedReplayAsync(fetched).ConfigureAwait(false);
                TagLoaded(activity, loaded);
                return loaded;
            }
        }

        activity?.SetTag("replay.source", "heroesprofile");
        loaded = await GetNextStandardReplayAsync();
        TagLoaded(activity, loaded);
        return loaded;
    }

    private static void TagLoaded(Activity activity, LoadedReplay loaded)
    {
        if (loaded == null)
        {
            activity?.SetTag("replay.empty", true);
            return;
        }

        HeroesReplayTelemetry.TagReplay(
            activity,
            loaded.FileInfo?.FullName,
            loaded.Replay?.Map,
            loaded.ReplayId,
            loaded.Replay?.ReplayVersion
        );
    }

    private async Task<RequestFetch> TryFetchRequestAsync()
    {
        try
        {
            RequestFetch fetched = await FetchRequestAsync().ConfigureAwait(false);
            if (fetched.Outage != null)
            {
                logger.LogError(
                    fetched.Outage.SourceException,
                    "Heroes Profile did not answer the download of a requested replay. The request stays queued."
                );
            }

            return fetched;
        }
        catch (OperationCanceledException) when (provider.Token.IsCancellationRequested)
        {
            return RequestFetch.Skipped;
        }
        catch (Exception e)
        {
            logger.LogCritical(e, "Could not provide a requested replay file. It stays queued.");
            return RequestFetch.Skipped;
        }
    }

    private async Task<LoadedReplay> LoadRequestedReplayAsync(RequestFetch fetched)
    {
        if (fetched.File == null)
        {
            return null;
        }

        try
        {
            Replay replay = await replayLoader
                .LoadAsync(fetched.File.FullName)
                .ConfigureAwait(false);
            return new LoadedReplay
            {
                ReplayId = fetched.Item.HeroesProfileReplay.Id,
                RewardQueueItem = fetched.Item,
                HeroesProfileReplay = fetched.Item.HeroesProfileReplay,
                FileInfo = fetched.File,
                Replay = replay,
            };
        }
        catch (OperationCanceledException) when (provider.Token.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception e)
        {
            logger.LogCritical(e, "Could not load requested replay {Path}.", fetched.File.FullName);
            return null;
        }
    }

    /// <summary>
    /// The first due request's replay, on disk before the request leaves
    /// <c>Data\requests.json</c> (#351). A download that can succeed later keeps the request
    /// queued with a backoff. One that never can (Heroes Profile answers 404 or 410, or 403
    /// <c>replay_deleted</c>, or the replay is below the supported patch line) fails the request
    /// and records a cancel that
    /// <c>twitch connect</c> sends. A stop leaves the request as it was.
    /// </summary>
    private async Task<RequestFetch> FetchRequestAsync()
    {
        DateTimeOffset now = clock();
        RewardQueueItem item = await requestQueue.PeekDownloadAsync(now).ConfigureAwait(false);
        if (item == null)
        {
            return RequestFetch.None;
        }

        RedemptionEnd recorded;
        try
        {
            recorded = RedemptionDispositionLog.Recorded(
                DispositionsPath(),
                item.Request?.RedemptionId ?? Guid.Empty
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Another role is appending to it. Whether this redemption was already settled is
            // not known, so nothing is downloaded or dequeued this pass.
            logger.LogWarning(
                "Could not read {File}. Request '{Title}' stays queued for the next pass. {Error}",
                RedemptionDispositionLog.FileName,
                item.Request?.RewardTitle,
                e.Message
            );
            return RequestFetch.Skipped;
        }

        if (recorded == RedemptionEnd.Fulfill)
        {
            // Played and verified already, by a pass that was stopped before the request left
            // the queue. It is not downloaded or played again.
            RequestCompletion done = await requestQueue
                .CompleteDownloadAsync(item, publish: null)
                .ConfigureAwait(false);
            logger.LogInformation(
                "Request '{Title}' for replay {ReplayId} was already played and verified. It left the queue without a download ({Completion}).",
                item.Request?.RewardTitle,
                RequestedId(item),
                done
            );
            return RequestFetch.Skipped;
        }

        if (recorded == RedemptionEnd.Cancel)
        {
            // A pass recorded the cancel and was stopped before the request left the queue.
            await FailRequestAsync(
                    item,
                    item.Download?.FailureReason ?? "its replay could not be downloaded",
                    cancelRecorded: true
                )
                .ConfigureAwait(false);
            return RequestFetch.Skipped;
        }

        HeroesProfileReplay replay = item.HeroesProfileReplay;
        if (replay == null || replay.Id <= 0)
        {
            await FailRequestAsync(item, "the request names no replay").ConfigureAwait(false);
            return RequestFetch.Skipped;
        }

        if (
            !RequestDownloadRetry.OnSupportedLine(
                replay.GameVersion,
                settings.Spectate?.VersionsSupported,
                settings.Spectate?.MinimumGameVersion
            )
        )
        {
            string floor = settings.Spectate?.MinimumGameVersion;
            await FailRequestAsync(
                    item,
                    string.IsNullOrWhiteSpace(floor)
                        ? $"client {replay.GameVersion} is no longer supported"
                        : $"client {replay.GameVersion} is older than the supported patch line ({floor} or newer)"
                )
                .ConfigureAwait(false);
            return RequestFetch.Skipped;
        }

        await heroesProfileService.EnrichRankAsync(replay, provider.Token).ConfigureAwait(false);
        // The file name carries the rank, which the lookup may fill in differently on a later
        // pass. A replay already in Data\Requests under any name is that request's file.
        FileInfo requested =
            ExistingRequestFile(replay.Id) ?? GetFileInfo(RequestsDirectory, replay);
        string partial = null;
        if (!requested.Exists)
        {
            try
            {
                partial = await DownloadPartialAsync(replay, requested).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (provider.Token.IsCancellationRequested)
            {
                // A stop is not a failed attempt. The request stays queued as it was.
                throw;
            }
            catch (Exception e)
            {
                int? status =
                    e is ApiException api && api.ResponseStatusCode > 0
                        ? api.ResponseStatusCode
                        : null;
                // The body's error.code tells a deleted replay from a key problem (#361).
                string errorCode = (e as HeroesProfileApiException)?.ErrorCode;
                if (RequestDownloadRetry.Classify(status, errorCode) == RequestDownloadVerdict.Fail)
                {
                    await FailRequestAsync(
                            item,
                            $"Heroes Profile no longer has the replay file ({RequestDownloadRetry.Describe(status.Value, errorCode)})"
                        )
                        .ConfigureAwait(false);
                    return RequestFetch.Skipped;
                }

                await RetryLaterAsync(item, e, status, errorCode, now).ConfigureAwait(false);
                // Heroes Profile answered with a status: not an outage (#346).
                return status != null
                    ? RequestFetch.Skipped
                    : new RequestFetch(true, null, item, ExceptionDispatchInfo.Capture(e));
            }
        }

        RequestCompletion completion;
        try
        {
            completion = await requestQueue
                .CompleteDownloadAsync(item, () => Publish(requested, partial, item))
                .ConfigureAwait(false);
        }
        catch
        {
            DeletePartial(partial);
            throw;
        }

        switch (completion)
        {
            case RequestCompletion.Completed:
                requested.Refresh();
                if (partial != null)
                {
                    logger.LogInformation(
                        "Downloaded Heroes Profile replay {ReplayId} ({Bytes} bytes).",
                        replay.Id,
                        requested.Length
                    );
                }

                return new RequestFetch(true, requested, item, null);
            case RequestCompletion.NotQueued:
                DeletePartial(partial);
                logger.LogInformation(
                    "Request '{Title}' for replay {ReplayId} left the queue during its download. The replay was not kept.",
                    item.Request?.RewardTitle,
                    replay.Id
                );
                return RequestFetch.Skipped;
            default:
                // The queue was busy. The request stays queued and is downloaded again.
                DeletePartial(partial);
                return RequestFetch.Skipped;
        }
    }

    /// <summary>
    /// Runs under the queue lock, just before the request leaves the queue. The spectator reads
    /// the redemption beside the replay, so it is written before the replay appears (#165).
    /// </summary>
    private void Publish(FileInfo requested, string partial, RewardQueueItem item)
    {
        StoreRequest(requested, item);
        if (partial != null)
        {
            File.Move(partial, requested.FullName, overwrite: true);
        }
    }

    private async Task RetryLaterAsync(
        RewardQueueItem item,
        Exception error,
        int? status,
        string errorCode,
        DateTimeOffset now
    )
    {
        string reason = status is int code
            ? RequestDownloadRetry.Describe(code, errorCode)
            : $"{error.GetType().Name}: {error.Message}";
        RequestDownload download = await requestQueue
            .RetryDownloadLaterAsync(item, reason, now)
            .ConfigureAwait(false);
        logger.LogWarning(
            "Download of requested replay {ReplayId} for {Login} failed ({Reason}, attempt {Attempts}). The request stays queued and is tried again at {NextAttempt:o}. The redemption is not refunded.",
            RequestedId(item),
            item.Request?.Login,
            reason,
            download?.Attempts,
            download?.NextAttemptAt
        );
    }

    /// <summary>
    /// Gives a request up for good. The download role does not call Twitch: it records a cancel
    /// in <c>Data\redemption-dispositions.txt</c> first, and <c>twitch connect</c> sends it
    /// (<see cref="RedemptionFulfiller"/>), which returns the viewer's points.
    /// </summary>
    private async Task FailRequestAsync(
        RewardQueueItem item,
        string reason,
        bool cancelRecorded = false
    )
    {
        bool refund = item.Request?.RedemptionId is Guid id && id != Guid.Empty;
        if (refund && !cancelRecorded)
        {
            RedemptionDispositionLog.Append(
                DispositionsPath(),
                RequestedId(item),
                item.Request,
                RedemptionEnd.Cancel
            );
        }

        bool removed = await requestQueue
            .FailDownloadAsync(item, reason, refund, clock())
            .ConfigureAwait(false);
        logger.LogWarning(
            "Request '{Title}' from {Login} for replay {ReplayId} (redemption {RedemptionId}) cannot be played: {Reason}. {Outcome}",
            item.Request?.RewardTitle,
            item.Request?.Login,
            RequestedId(item),
            item.Request?.RedemptionId,
            reason,
            refund
                ? "A cancel was recorded for twitch connect, which returns the points."
                : "It has no redemption to refund."
        );
        if (!removed)
        {
            logger.LogWarning(
                "The request queue was busy. Request '{Title}' leaves it on the next pass.",
                item.Request?.RewardTitle
            );
        }
    }

    private static int? RequestedId(RewardQueueItem item) =>
        item?.HeroesProfileReplay?.Id > 0 ? item.HeroesProfileReplay.Id : item?.Request?.ReplayId;

    private FileInfo ExistingRequestFile(int replayId)
    {
        string separator = settings.StormReplay.Seperator;
        string extension = settings.StormReplay.FileExtension;
        if (string.IsNullOrEmpty(separator) || string.IsNullOrEmpty(extension))
        {
            return null;
        }

        string prefix = replayId.ToString(CultureInfo.InvariantCulture) + separator;
        return RequestsDirectory
            .GetFiles(prefix + "*" + extension)
            .Where(file => file.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private string DispositionsPath() =>
        Path.Combine(settings.Location.DataDirectory, RedemptionDispositionLog.FileName);

    /// <summary>
    /// One pass over the request queue. <c>Taken</c> is false when no request was due.
    /// <c>File</c> is the replay when the request left the queue with it on disk.
    /// <c>Outage</c> is a download Heroes Profile did not answer.
    /// </summary>
    private sealed record RequestFetch(
        bool Taken,
        FileInfo File,
        RewardQueueItem Item,
        ExceptionDispatchInfo Outage
    )
    {
        public static readonly RequestFetch None = new(false, null, null, null);

        public static readonly RequestFetch Skipped = new(true, null, null, null);
    }

    private async Task<LoadedReplay> GetNextStandardReplayAsync()
    {
        try
        {
            HeroesProfileReplay heroesProfileReplay = await GetNextReplayAsync()
                .ConfigureAwait(false);

            if (heroesProfileReplay != null)
            {
                await heroesProfileService
                    .EnrichRankAsync(heroesProfileReplay, provider.Token)
                    .ConfigureAwait(false);

                FileInfo fileInfo = GetFileInfo(StandardDirectory, heroesProfileReplay);

                if (!fileInfo.Exists)
                {
                    await DownloadReplayAsync(heroesProfileReplay, fileInfo).ConfigureAwait(false);
                }

                fileInfo.Refresh();

                Replay replay = await replayLoader
                    .LoadAsync(fileInfo.FullName)
                    .ConfigureAwait(false);

                return new LoadedReplay
                {
                    ReplayId = heroesProfileReplay.Id,
                    HeroesProfileReplay = heroesProfileReplay,
                    FileInfo = fileInfo,
                    Replay = replay,
                    RewardQueueItem = null,
                };
            }
        }
        catch (OperationCanceledException) when (provider.Token.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception e)
        {
            logger.LogCritical(e, "Could not provide a Replay file using HeroesProfile API.");
        }

        return null;
    }

    private void StoreRequest(FileInfo replayFile, RewardQueueItem item)
    {
        try
        {
            CachedRequestReward.Write(replayFile.FullName, item);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                e,
                "Could not store the request beside replay {ReplayId}.",
                item.HeroesProfileReplay?.Id
            );
        }
    }

    /// <summary>
    /// The download goes to a <c>.part</c> file that is renamed when it is complete.
    /// A failed or cancelled download leaves no <c>.StormReplay</c> for the next run to load.
    /// </summary>
    private async Task DownloadReplayAsync(HeroesProfileReplay replay, FileInfo fileInfo)
    {
        string partial = await DownloadPartialAsync(replay, fileInfo).ConfigureAwait(false);
        try
        {
            File.Move(partial, fileInfo.FullName, overwrite: true);
        }
        catch
        {
            DeletePartial(partial);
            throw;
        }

        fileInfo.Refresh();
        logger.LogInformation(
            "Downloaded Heroes Profile replay {ReplayId} ({Bytes} bytes).",
            replay.Id,
            fileInfo.Length
        );
    }

    /// <summary>
    /// The whole replay in <paramref name="fileInfo"/>'s <c>.part</c> file, which the caller
    /// renames into place. A failed or cancelled download leaves no <c>.part</c> file.
    /// </summary>
    private async Task<string> DownloadPartialAsync(HeroesProfileReplay replay, FileInfo fileInfo)
    {
        using Activity session = HeroesReplayTelemetry.BeginReplaySession(replay.Id);
        using Activity activity = HeroesReplayTelemetry.StartSpan(
            "heroesreplay.replay.download",
            session
        );
        HeroesReplayTelemetry.TagReplay(activity, map: replay.Map, replayId: replay.Id);

        string partial = PartialPath(fileInfo);
        try
        {
            await WritePartialAsync(replay, partial).ConfigureAwait(false);
        }
        // A stop cancels the token. That is not an outage, so the resume flag stays for the next run.
        catch (Exception e)
            when (!provider.Token.IsCancellationRequested
                && heroesProfileResume != null
                && heroesProfileResume.Consume()
            )
        {
            logger.LogWarning(
                e,
                "Connectivity restored. Retrying Heroes Profile download of {ReplayId} once.",
                replay.Id
            );
            await WritePartialAsync(replay, partial).ConfigureAwait(false);
        }

        ReplaySessionFile.Publish(session, replay.Id);
        if (session != null)
        {
            logger.LogInformation(
                "Replay session {ReplayId} trace {TraceId}.",
                replay.Id,
                session.TraceId
            );
        }

        return partial;
    }

    private async Task WritePartialAsync(HeroesProfileReplay replay, string partial)
    {
        try
        {
            await using FileStream file = new FileStream(
                partial,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None
            );
            await heroesProfileService
                .DownloadReplayAsync(replay.Id, file, provider.Token)
                .ConfigureAwait(false);
            await file.FlushAsync(provider.Token).ConfigureAwait(false);
        }
        catch
        {
            DeletePartial(partial);
            throw;
        }
    }

    private static string PartialPath(FileInfo fileInfo) => fileInfo.FullName + ".part";

    private void DeletePartial(string partial)
    {
        if (partial == null)
        {
            return;
        }

        try
        {
            File.Delete(partial);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not remove the partial download {Path}.", partial);
        }
    }

    private FileInfo GetFileInfo(DirectoryInfo directory, HeroesProfileReplay replay)
    {
        var path = directory.FullName;

        var segments = new string[]
        {
            $"{replay.Id}",
            replay.GameType,
            replay.Rank ?? "Unknown",
            replay.Map,
            replay.Fingerprint,
            settings.StormReplay.FileExtension,
        };

        var name = string.Join(settings.StormReplay.Seperator, segments);
        return new FileInfo(Path.Combine(path, name));
    }

    private async Task<HeroesProfileReplay> GetNextReplayAsync()
    {
        if (listBackoff.Paused && !listBackoff.TryList(clock()))
        {
            // Heroes Profile refused the key: no list call until the next turn or a passing
            // probe (#358). Not an outage, and nothing to log again.
            return null;
        }

        try
        {
            // A refused key is not retried here: the same key gets the same answer (#358).
            HeroesProfileReplay found = await ResilienceRetry
                .Constant<HeroesProfileReplay>(
                    retries: 60,
                    delay: settings.HeroesProfileApi.APIRetryWaitTime,
                    retry: outcome =>
                        !listBackoff.Paused
                        && ResilienceRetry.Failed(outcome, replay => replay == null)
                )
                .ExecuteAsync(
                    _ => new ValueTask<HeroesProfileReplay>(ListOnceAsync()),
                    provider.Token
                )
                .ConfigureAwait(false);

            if (
                found == null
                && !listBackoff.Paused
                && heroesProfileResume != null
                && heroesProfileResume.Consume()
            )
            {
                logger.LogInformation(
                    "Connectivity restored. Retrying Heroes Profile replay list once."
                );
                found = await ListOnceAsync().ConfigureAwait(false);
            }

            return found;
        }
        catch (OperationCanceledException) when (provider.Token.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception e)
        {
            if (!listBackoff.Paused && heroesProfileResume != null && heroesProfileResume.Consume())
            {
                logger.LogWarning(
                    e,
                    "Connectivity restored. Retrying Heroes Profile replay list once."
                );
                try
                {
                    return await ListOnceAsync().ConfigureAwait(false);
                }
                catch (Exception retryError)
                {
                    logger.LogError(retryError, "Could not get the next replay file.");
                    return null;
                }
            }

            logger.LogError(e, "Could not get the next replay file.");
        }

        return null;
    }

    /// <summary>
    /// The next Standard replay on the newest installed client (the current patch). Upload order
    /// does not follow the client build, so the listing reads ahead to the end of the list for one.
    /// With none on that build, it falls back to the first replay on an older build: installed,
    /// or on the supported patch line and not installed (HeroesSwitcher has Blizzard download
    /// it), unless that build is held after a failed download.
    /// </summary>
    private async Task<HeroesProfileReplay> ListOnceAsync()
    {
        IReadOnlyList<string> installed =
            installedVersionSource != null
                ? installedVersionSource()
                : InstalledClientCatalog.FileVersions(settings.Location?.GameInstallDirectory);
        IReadOnlyList<string> held = ClientDownloadHold.ActiveIn(
            settings.Location?.DataDirectory,
            DateTimeOffset.UtcNow,
            settings.Spectate?.BuildDownloadHold ?? TimeSpan.Zero
        );
        string minimumVersion = settings.Spectate?.MinimumGameVersion;
        HeroesProfileReplay fallback = null;
        for (int pageIndex = 0; pageIndex < 40; pageIndex++)
        {
            provider.Token.ThrowIfCancellationRequested();
            int currentMin = MinReplayId;
            ReplayListing page = await heroesProfileService
                .ListPageAsync(currentMin)
                .ConfigureAwait(false);
            listBackoff.Record(page, clock());
            if (page?.RejectedStatus != null)
            {
                return null;
            }

            ReplayListing launchable = ReplayDownloadPick.Launchable(
                page,
                installed,
                held,
                minimumVersion
            );
            List<HeroesProfileReplay> candidates = launchable
                .Playable.Where(replay =>
                    replay != null
                    && replay.Id > currentMin
                    && settings.HeroesProfileApi.IsAllowedGameType(replay.GameType)
                )
                .OrderBy(replay => replay.Id)
                .ToList();
            HeroesProfileReplay first = candidates.FirstOrDefault();
            if (first != null && await CatchUpAsync(currentMin, first).ConfigureAwait(false))
            {
                fallback = null;
                continue;
            }

            HeroesProfileReplay found = ReplayDownloadPick.FirstOnCurrentPatch(
                candidates,
                installed
            );
            if (found != null)
            {
                logger.LogInformation(
                    "Replay {ReplayId} on {GameVersion} found. MinReplayId = {MinReplayId}",
                    found.Id,
                    found.GameVersion,
                    currentMin
                );
                MinReplayId = found.Id;
                return found;
            }

            fallback ??= first;
            int? next = ReplayListCursor.AfterPage(currentMin, launchable);
            if (next is not int advanced)
            {
                break;
            }

            logger.LogInformation(
                first == null
                    ? "No playable replay after {MinReplayId}. Continuing after {Next}."
                    : "No current-patch replay after {MinReplayId}. Continuing after {Next}.",
                currentMin,
                advanced
            );
            MinReplayId = advanced;
        }

        if (fallback == null)
        {
            return null;
        }

        logger.LogInformation(
            "No replay on the current patch is listed. Taking replay {ReplayId} on previous build {GameVersion}.",
            fallback.Id,
            fallback.GameVersion
        );
        MinReplayId = fallback.Id;
        return fallback;
    }

    /// <summary>
    /// Moves the cursor near the newest replays when <paramref name="found"/> is too old (#205).
    /// True when the cursor moved and the listing should start again from there.
    /// </summary>
    private async Task<bool> CatchUpAsync(int currentMin, HeroesProfileReplay found)
    {
        HeroesProfileApiSettings api = settings.HeroesProfileApi;
        if (api.StandardMaxReplayAge <= TimeSpan.Zero)
        {
            // Zero turns catch-up off: no newest-id lookup and no log line per listing (#217).
            return false;
        }

        DateTime now = DateTime.UtcNow;
        if (StandardCatchUp.Age(found, now) is not TimeSpan age || age <= api.StandardMaxReplayAge)
        {
            return false;
        }

        int newest = await heroesProfileService.GetMaxReplayIdAsync().ConfigureAwait(false);
        int? jump = StandardCatchUp.JumpTo(
            currentMin,
            found,
            newest,
            now,
            api.StandardMaxReplayAge,
            api.StandardCatchUpWindow
        );
        if (jump is not int target)
        {
            logger.LogWarning(
                "Standard replay {ReplayId} is {AgeHours:F0} h old; the newest is {Newest} ({Gap} ids ahead). The cursor stays.",
                found.Id,
                age.TotalHours,
                newest,
                newest - found.Id
            );
            return false;
        }

        logger.LogWarning(
            "Standard replay {ReplayId} is {AgeHours:F0} h old; the newest is {Newest} ({Gap} ids ahead). Jumping to {Target}.",
            found.Id,
            age.TotalHours,
            newest,
            newest - found.Id,
            target
        );
        MinReplayId = target;
        return true;
    }

    private WaitingReplays UnspectatedOnDisk()
    {
        if (!StandardDirectory.Exists)
        {
            return default;
        }

        var onDisk = new List<int>();
        var files = new Dictionary<int, FileInfo>();
        foreach (FileInfo file in StandardDirectory.GetFiles(settings.StormReplay.WildCard))
        {
            if (replayHelper.TryGetReplayId(file.Name, out int id))
            {
                onDisk.Add(id);
                files.TryAdd(id, file);
            }
        }

        DateTime now = DateTime.UtcNow;
        WaitingReplays waiting = SpectateQueue.CountWaiting(
            onDisk,
            ReadNotWaitingIds(),
            id => IsInsideMediaWindow(id, files[id], now)
        );
        foreach (int gone in mediaFacts.Keys.Where(id => !files.ContainsKey(id)).ToList())
        {
            mediaFacts.Remove(gone);
        }

        return waiting;
    }

    /// <summary>
    /// The media policy's game-date window, the rule behind <c>records False (expired)</c>
    /// (#280). A replay whose game date cannot be read counts as inside the window, as every
    /// waiting replay did before.
    /// </summary>
    private bool IsInsideMediaWindow(int replayId, FileInfo file, DateTime utcNow)
    {
        if (!mediaFacts.TryGetValue(replayId, out ReplayMediaPolicyInput facts))
        {
            facts = ReadMediaFacts(replayId, file);
            mediaFacts[replayId] = facts;
        }

        return !ReplayMediaPolicy.IsPastWindow(facts, settings.ReplayMedia, utcNow);
    }

    /// <summary>The facts spectate would judge, read once per file: a file does not change.</summary>
    private ReplayMediaPolicyInput ReadMediaFacts(int replayId, FileInfo file)
    {
        Replay header = headerSource(file.FullName);
        if (header == null || header.Timestamp == default)
        {
            logger.LogWarning(
                "Could not read the game date of waiting replay {ReplayId}. It counts toward the download limit.",
                replayId
            );
        }

        return ReplayMediaFacts.From(
            new LoadedReplay
            {
                ReplayId = replayId,
                FileInfo = file,
                Replay = header,
                HeroesProfileReplay = new HeroesProfileReplay
                {
                    Id = replayId,
                    Rank = RankImage.RankFromCacheFileName(
                        file.Name,
                        settings.StormReplay.Seperator
                    ),
                },
            },
            alreadyPublished: false,
            alreadyScheduled: false,
            inOutbox: false
        );
    }

    private List<int> ReadNotWaitingIds()
    {
        var ids = new List<int>();
        ReadIds(ids, SpectateQueue.SpectatedFileName);
        ReadIds(ids, SpectateQueue.BelowFloorFileName);
        ReadIds(ids, SpectateQueue.QuarantineFileName);
        ReadIds(ids, SpectateQueue.DeferredFileName);
        return ids;
    }

    private void ReadIds(List<int> ids, string fileName)
    {
        string path = Path.Combine(settings.Location.DataDirectory, fileName);
        if (!File.Exists(path))
        {
            return;
        }

        foreach (string line in File.ReadLines(path))
        {
            if (SpectateQueue.TryParseId(line, out int id))
            {
                ids.Add(id);
            }
        }
    }

    private int CachedReplayLimit =>
        settings.HeroesProfileApi?.CachedReplayLimit > 0
            ? settings.HeroesProfileApi.CachedReplayLimit
            : 5;
}
