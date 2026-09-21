using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Connectivity;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Observer;
using HeroesReplay.Core.Services.Providers;
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

    public Engine(
        ILogger<Engine> logger,
        IGameManager gameManager,
        IGameData gameData,
        IReplayProvider replayProvider,
        CancellationTokenProvider consoleTokenProvider,
        SpectatorStatusStore statusStore,
        IConnectivityWatchdog connectivityWatchdog
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
            LoadedReplay loadedReplay = await replayProvider.TryLoadNextReplayAsync();

            if (loadedReplay != null)
            {
                HeroesReplayTelemetry.TagReplay(
                    replayActivity,
                    loadedReplay.FileInfo?.FullName,
                    loadedReplay.Replay?.Map,
                    loadedReplay.ReplayId,
                    loadedReplay.Replay?.ReplayVersion
                );
                try
                {
                    await gameManager.LaunchAndSpectate(loadedReplay);
                }
                catch (Exception e)
                {
                    logger.LogError(
                        e,
                        "Spectate failed for replay {ReplayId}. Continuing.",
                        loadedReplay.ReplayId
                    );
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
}
