using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Telemetry;
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
    private LoadedReplay staged;
    private int? heldBackId;
    private int minReplayId;
    private Func<IReadOnlyList<string>> installedVersionSource;
    private Func<string, Replay> headerSource = ReplayHeader.Load;
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
    }

    internal void UseInstalledVersions(Func<IReadOnlyList<string>> source)
    {
        installedVersionSource = source;
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
            RewardQueueItem item = await requestQueue.DequeueItemAsync().ConfigureAwait(false);
            if (item?.HeroesProfileReplay != null)
            {
                await heroesProfileService
                    .EnrichRankAsync(item.HeroesProfileReplay, provider.Token)
                    .ConfigureAwait(false);
                FileInfo requested = GetFileInfo(RequestsDirectory, item.HeroesProfileReplay);
                // The spectator reads the redemption beside the replay. Write it before the
                // replay appears, so the file is never seen without its request (#165).
                StoreRequest(requested, item);
                if (!requested.Exists)
                {
                    try
                    {
                        await DownloadReplayAsync(item.HeroesProfileReplay, requested)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        ForgetRequest(requested);
                        throw;
                    }
                }

                return true;
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
            RewardQueueItem item = await requestQueue.DequeueItemAsync();

            if (item != null)
            {
                logger.LogInformation("Reward request item found, loading...");
                activity?.SetTag("replay.source", "request");
                loaded = await GetNextRequestedReplayAsync(item);
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

    private async Task<LoadedReplay> GetNextRequestedReplayAsync(RewardQueueItem item)
    {
        try
        {
            if (item != null)
            {
                await heroesProfileService
                    .EnrichRankAsync(item.HeroesProfileReplay, provider.Token)
                    .ConfigureAwait(false);

                FileInfo fileInfo = GetFileInfo(RequestsDirectory, item.HeroesProfileReplay);

                if (!fileInfo.Exists)
                {
                    await DownloadReplayAsync(item.HeroesProfileReplay, fileInfo)
                        .ConfigureAwait(false);
                }

                fileInfo.Refresh();
                StoreRequest(fileInfo, item);

                Replay replay = await replayLoader
                    .LoadAsync(fileInfo.FullName)
                    .ConfigureAwait(false);

                return new LoadedReplay
                {
                    ReplayId = item.HeroesProfileReplay.Id,
                    RewardQueueItem = item,
                    HeroesProfileReplay = item.HeroesProfileReplay,
                    FileInfo = fileInfo,
                    Replay = replay,
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

    private void ForgetRequest(FileInfo replayFile)
    {
        try
        {
            CachedRequestReward.Delete(replayFile.FullName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                e,
                "Could not remove the request beside {Path}.",
                replayFile.FullName
            );
        }
    }

    private async Task DownloadReplayAsync(HeroesProfileReplay replay, FileInfo fileInfo)
    {
        using Activity session = HeroesReplayTelemetry.BeginReplaySession(replay.Id);
        using Activity activity = HeroesReplayTelemetry.StartSpan(
            "heroesreplay.replay.download",
            session
        );
        HeroesReplayTelemetry.TagReplay(activity, map: replay.Map, replayId: replay.Id);

        try
        {
            await WriteDownloadAsync(replay, fileInfo).ConfigureAwait(false);
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
            await WriteDownloadAsync(replay, fileInfo).ConfigureAwait(false);
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
    }

    /// <summary>
    /// The download goes to a <c>.part</c> file that is renamed when it is complete.
    /// A failed or cancelled download leaves no <c>.StormReplay</c> for the next run to load.
    /// </summary>
    private async Task WriteDownloadAsync(HeroesProfileReplay replay, FileInfo fileInfo)
    {
        string partial = PartialPath(fileInfo);
        try
        {
            await using (
                FileStream file = new FileStream(
                    partial,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None
                )
            )
            {
                await heroesProfileService
                    .DownloadReplayAsync(replay.Id, file, provider.Token)
                    .ConfigureAwait(false);
                await file.FlushAsync(provider.Token).ConfigureAwait(false);
            }

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

    private static string PartialPath(FileInfo fileInfo) => fileInfo.FullName + ".part";

    private void DeletePartial(string partial)
    {
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
        try
        {
            HeroesProfileReplay found = await ResilienceRetry
                .Constant<HeroesProfileReplay>(
                    retries: 60,
                    delay: settings.HeroesProfileApi.APIRetryWaitTime,
                    retry: outcome => ResilienceRetry.Failed(outcome, replay => replay == null)
                )
                .ExecuteAsync(
                    _ => new ValueTask<HeroesProfileReplay>(ListOnceAsync()),
                    provider.Token
                )
                .ConfigureAwait(false);

            if (found == null && heroesProfileResume != null && heroesProfileResume.Consume())
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
            if (heroesProfileResume != null && heroesProfileResume.Consume())
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
