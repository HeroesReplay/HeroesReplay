using HeroesReplay.Core.Models;
using HeroesReplay.Core.Providers;
using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Services.Twitch;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class Engine : IEngine
    {
        private readonly ILogger<Engine> logger;
        private readonly ITwitchBot twitchBot;
        private readonly IGameManager manager;
        private readonly IGameData gameData;
        private readonly IReplayProvider replayProvider;
        private readonly ConsoleTokenProvider consoleTokenProvider;

        public Engine(
            ILogger<Engine> logger,
            ITwitchBot twitchBot,
            IGameManager gameManager,
            IGameData gameData,
            IReplayProvider replayProvider,
            ConsoleTokenProvider consoleTokenProvider)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.twitchBot = twitchBot ?? throw new ArgumentNullException(nameof(twitchBot));
            this.manager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
            this.replayProvider = replayProvider ?? throw new ArgumentNullException(nameof(replayProvider));
            this.consoleTokenProvider = consoleTokenProvider ?? throw new ArgumentNullException(nameof(consoleTokenProvider));
        }

        public async Task RunAsync()
        {
            try
            {
                await Initialize();
                await Task.WhenAll(Task.Run(SpectatorAsync, consoleTokenProvider.Token), Task.Run(TwitchBotAsync, consoleTokenProvider.Token));
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception e)
            {
                logger.LogError(e, "An unexpected error");
            }
        }

        private async Task Initialize()
        {
            await gameData.LoadDataAsync();
        }

        private async Task TwitchBotAsync()
        {
            await twitchBot.InitializeAsync();
        }

        private async Task SpectatorAsync()
        {
            while (!consoleTokenProvider.Token.IsCancellationRequested)
            {
                LoadedReplay loadedReplay = await replayProvider.TryLoadNextReplayAsync();

                if (loadedReplay != null)
                {
                    await manager.LaunchAndSpectate(loadedReplay);
                }
            }
        }
    }
}