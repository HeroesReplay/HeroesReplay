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
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Status;
using Microsoft.Extensions.Logging;

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

        if (UnspectatedOnDisk() >= CachedReplayLimit)
        {
            logger.LogInformation(
                "{Count} replays are already waiting to be spectated. Not downloading another.",
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
        FileInfo fileInfo = GetFileInfo(StandardDirectory, replay);
        if (!fileInfo.Exists)
        {
            await DownloadReplayAsync(replay, fileInfo).ConfigureAwait(false);
        }

        return true;
    }

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

    private async Task<HeroesProfileReplay> ListOnceAsync()
    {
        IReadOnlyList<string> installed = InstalledClientCatalog.FileVersions(
            settings.Location?.GameInstallDirectory
        );
        for (int pageIndex = 0; pageIndex < 40; pageIndex++)
        {
            provider.Token.ThrowIfCancellationRequested();
            int currentMin = MinReplayId;
            ReplayListing page = await heroesProfileService
                .ListPageAsync(currentMin)
                .ConfigureAwait(false);
            ReplayListing launchable = ReplayDownloadPick.Launchable(page, installed);
            HeroesProfileReplay found = launchable
                ?.Playable?.Where(replay =>
                    replay != null
                    && replay.Id > currentMin
                    && settings.HeroesProfileApi.IsAllowedGameType(replay.GameType)
                )
                .OrderBy(replay => replay.Id)
                .FirstOrDefault();
            if (found != null)
            {
                logger.LogInformation("Replay found. MinReplayId = {MinReplayId}", currentMin);
                MinReplayId = found.Id;
                return found;
            }

            int? next = ReplayListCursor.AfterRejectedPage(currentMin, launchable);
            if (next is not int advanced)
            {
                return null;
            }

            logger.LogInformation(
                "No playable replay after {MinReplayId}. Continuing after {Next}.",
                currentMin,
                advanced
            );
            MinReplayId = advanced;
        }

        return null;
    }

    private int UnspectatedOnDisk()
    {
        if (!StandardDirectory.Exists)
        {
            return 0;
        }

        var onDisk = new List<int>();
        foreach (FileInfo file in StandardDirectory.GetFiles(settings.StormReplay.WildCard))
        {
            if (replayHelper.TryGetReplayId(file.Name, out int id))
            {
                onDisk.Add(id);
            }
        }

        return SpectateQueue.CountWaiting(onDisk, ReadNotWaitingIds(), CachedReplayLimit);
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
