using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Connectivity;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Observer;
using HeroesReplay.Core.Services.Providers;
using HeroesReplay.Core.Services.SelfUpdate;
using HeroesReplay.Core.Services.Shared;
using HeroesReplay.Core.Services.Status;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core;

public class Engine : IEngine
{
    private readonly ILogger<Engine> logger;
    private readonly IGameManager gameManager;
    private readonly IGameData gameData;
    private readonly IReplayProvider replayProvider;
    private readonly CancellationTokenProvider consoleTokenProvider;
    private readonly SpectatorStatusStore statusStore;
    private readonly IConnectivityWatchdog connectivityWatchdog;
    private readonly IReplayResume replayResume;
    private readonly IReplayLoader replayLoader;
    private readonly IReleaseUpdateGate releaseUpdate;
    private LoadedReplay preparedNext;

    public Engine(
        ILogger<Engine> logger,
        IGameManager gameManager,
        IGameData gameData,
        IReplayProvider replayProvider,
        CancellationTokenProvider consoleTokenProvider,
        SpectatorStatusStore statusStore,
        IConnectivityWatchdog connectivityWatchdog,
        IReplayResume replayResume,
        IReplayLoader replayLoader,
        IReleaseUpdateGate releaseUpdate
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
        this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
        this.replayProvider =
            replayProvider ?? throw new ArgumentNullException(nameof(replayProvider));
        this.consoleTokenProvider =
            consoleTokenProvider ?? throw new ArgumentNullException(nameof(consoleTokenProvider));
        this.statusStore = statusStore ?? throw new ArgumentNullException(nameof(statusStore));
        this.connectivityWatchdog =
            connectivityWatchdog ?? throw new ArgumentNullException(nameof(connectivityWatchdog));
        this.replayResume = replayResume ?? throw new ArgumentNullException(nameof(replayResume));
        this.replayLoader = replayLoader ?? throw new ArgumentNullException(nameof(replayLoader));
        this.releaseUpdate =
            releaseUpdate ?? throw new ArgumentNullException(nameof(releaseUpdate));
    }

    public async Task RunAsync()
    {
        try
        {
            await Initialize();
            await Task.WhenAll(
                Task.Run(SpectatorAsync, consoleTokenProvider.Token),
                Task.Run(ConnectivityAsync, consoleTokenProvider.Token)
            );
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            logger.LogError(e, "An unexpected error in the replay engine.");
        }
    }

    private async Task Initialize()
    {
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.initialize");
        await gameData.LoadDataAsync();
    }

    private async Task ConnectivityAsync()
    {
        await connectivityWatchdog.RunAsync(consoleTokenProvider.Token);
    }

    private async Task SpectatorAsync()
    {
        while (!consoleTokenProvider.Token.IsCancellationRequested)
        {
            using Activity replayActivity = HeroesReplayTelemetry.StartSpan("heroesreplay.replay");
            LoadedReplay loadedReplay = await TakeResumedReplayAsync().ConfigureAwait(false);
            if (loadedReplay != null)
            {
                ReturnPreparedNext();
            }
            else if (preparedNext != null)
            {
                loadedReplay = preparedNext;
                preparedNext = null;
                logger.LogInformation(
                    "Playing replay {ReplayId} loaded during the previous report.",
                    loadedReplay.ReplayId
                );
            }
            else
            {
                loadedReplay = await replayProvider.TryLoadNextReplayAsync();
            }

            if (loadedReplay != null)
            {
                HeroesReplayTelemetry.TagReplay(
                    replayActivity,
                    loadedReplay.FileInfo?.FullName,
                    loadedReplay.Replay?.Map,
                    loadedReplay.ReplayId,
                    loadedReplay.Replay?.ReplayVersion
                );
                Task<LoadedReplay> nextLoad = null;
                try
                {
                    await gameManager.LaunchAndSpectate(
                        loadedReplay,
                        () =>
                        {
                            nextLoad = StartNextLoad();
                            return Task.CompletedTask;
                        }
                    );
                }
                catch (Exception e)
                {
                    logger.LogError(
                        e,
                        "Spectate failed for replay {ReplayId}. Continuing.",
                        loadedReplay.ReplayId
                    );
                }

                await StorePreparedNextAsync(nextLoad).ConfigureAwait(false);
                if (
                    await releaseUpdate
                        .TryStageAsync(consoleTokenProvider.Token)
                        .ConfigureAwait(false)
                )
                {
                    logger.LogInformation(
                        "Stopping after this replay so the new release can replace this install."
                    );
                    break;
                }

                continue;
            }

            replayActivity?.SetTag("replay.empty", true);

            if (!replayProvider.ContinuesWhenEmpty)
            {
                statusStore.MarkIdle();
                break;
            }

            statusStore.MarkIdle();
            await Task.Delay(TimeSpan.FromSeconds(5), consoleTokenProvider.Token);
        }
    }

    private Task<LoadedReplay> StartNextLoad()
    {
        if (replayResume.HasPending())
        {
            logger.LogInformation(
                "A replay is waiting to resume. The next file stays in the queue."
            );
            return null;
        }

        logger.LogInformation("Loading the next replay during the report scenes.");
        return replayProvider.TryLoadNextReplayAsync();
    }

    private async Task StorePreparedNextAsync(Task<LoadedReplay> nextLoad)
    {
        if (nextLoad == null)
        {
            return;
        }

        LoadedReplay prepared;
        try
        {
            prepared = await nextLoad.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not load the next replay during the report.");
            return;
        }

        if (prepared == null)
        {
            return;
        }

        if (replayResume.HasPending())
        {
            replayProvider.Requeue(prepared);
            return;
        }

        preparedNext = prepared;
    }

    private void ReturnPreparedNext()
    {
        if (preparedNext == null)
        {
            return;
        }

        replayProvider.Requeue(preparedNext);
        preparedNext = null;
    }

    private async Task<LoadedReplay> TakeResumedReplayAsync()
    {
        if (!replayResume.TryTake(out int replayId, out string replayPath))
        {
            return null;
        }

        logger.LogInformation(
            "Replaying {ReplayId} after connectivity returned ({Path}).",
            replayId,
            replayPath
        );
        Replay replay = await replayLoader.LoadAsync(replayPath).ConfigureAwait(false);
        if (replay == null)
        {
            logger.LogWarning("Could not load the replay requested after connectivity returned.");
            return null;
        }

        return new LoadedReplay
        {
            ReplayId = replayId,
            Replay = replay,
            FileInfo = new FileInfo(replayPath),
        };
    }
}
