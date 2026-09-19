using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Shared;
using HeroesReplay.Core.Services.Twitch.Rewards;
using Microsoft.Extensions.Logging;
using Polly;

namespace HeroesReplay.Core.Services.Providers;

public class HeroesProfileProvider : IReplayProvider
{
    private readonly AppSettings settings;
    private readonly CancellationTokenProvider provider;
    private readonly ILogger<HeroesProfileProvider> logger;
    private readonly IReplayLoader replayLoader;
    private readonly IReplayHelper replayHelper;
    private readonly IHeroesProfileService heroesProfileService;
    private readonly IRequestQueue requestQueue;
    private int minReplayId;

    public bool ContinuesWhenEmpty => true;

    private int MinReplayId
    {
        get
        {
            if (minReplayId == default)
            {
                if (StandardDirectory.GetFiles(settings.StormReplay.WildCard).Any())
                {
                    FileInfo latest = StandardDirectory
                        .GetFiles(settings.StormReplay.WildCard)
                        .OrderByDescending(f =>
                            int.Parse(
                                Path.GetFileName(f.FullName).Split(settings.StormReplay.Seperator)[
                                    0
                                ]
                            )
                        )
                        .FirstOrDefault();

                    if (replayHelper.TryGetReplayId(latest.Name, out int replayId))
                    {
                        MinReplayId = replayId;
                    }
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
        AppSettings settings
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
    }

    public async Task<LoadedReplay> TryLoadNextReplayAsync()
    {
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.replay.load");
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
                FileInfo fileInfo = GetFileInfo(RequestsDirectory, item.HeroesProfileReplay);

                if (!fileInfo.Exists)
                {
                    await DownloadReplayAsync(item.HeroesProfileReplay, fileInfo)
                        .ConfigureAwait(false);
                }

                fileInfo.Refresh();

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
        catch (Exception e)
        {
            logger.LogCritical(e, "Could not provide a Replay file using HeroesProfile API.");
        }

        return null;
    }

    private async Task DownloadReplayAsync(HeroesProfileReplay replay, FileInfo fileInfo)
    {
        using Activity activity = HeroesReplayTelemetry.ActivitySource.StartActivity(
            "heroesreplay.replay.download"
        );
        activity?.SetTag("replay.id", replay.Id);
        activity?.SetTag("replay.map", replay.Map);

        await using (FileStream file = fileInfo.OpenWrite())
        {
            await heroesProfileService
                .DownloadReplayAsync(replay.Id, file, provider.Token)
                .ConfigureAwait(false);
            await file.FlushAsync(provider.Token).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Downloaded Heroes Profile replay {ReplayId} ({Bytes} bytes).",
            replay.Id,
            fileInfo.Length
        );
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
            return await Policy
                .Handle<Exception>()
                .OrResult<HeroesProfileReplay>(replay => replay == null)
                .WaitAndRetryAsync(60, retry => settings.HeroesProfileApi.APIRetryWaitTime)
                .ExecuteAsync(
                    async token =>
                    {
                        IEnumerable<HeroesProfileReplay> replays = await heroesProfileService
                            .GetReplaysByMinId(MinReplayId)
                            .ConfigureAwait(false);

                        if (replays != null && replays.Any())
                        {
                            logger.LogInformation("Finding replay that fits criteria.");

                            HeroesProfileReplay found = replays
                                .Where(r =>
                                    r.Id > MinReplayId
                                    && settings.HeroesProfileApi.IsAllowedGameType(r.GameType)
                                )
                                .OrderBy(x => x.Id)
                                .FirstOrDefault();

                            if (found == null)
                            {
                                logger.LogWarning(
                                    $"Replay not found with criteria. MinReplayId = {MinReplayId}"
                                );
                                MinReplayId = replays.Max(x => x.Id);
                            }
                            else
                            {
                                logger.LogInformation($"Replay found. MinReplayId = {MinReplayId}");
                                MinReplayId = found.Id;
                                return found;
                            }
                        }

                        return null;
                    },
                    provider.Token
                )
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not get the next replay file.");
        }

        return null;
    }
}
