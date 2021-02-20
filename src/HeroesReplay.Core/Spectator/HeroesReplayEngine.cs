﻿using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Providers;
using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Services.Obs;
using HeroesReplay.Core.Services.Twitch;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class HeroesReplayEngine : IHeroesReplayEngine
    {
        private readonly ILogger<HeroesReplayEngine> logger;
        private readonly AppSettings settings;
        private readonly ITwitchBot twitchBot;
        private readonly IGameManager gameManager;
        private readonly IGameData gameData;
        private readonly IReplayProvider replayProvider;
        private readonly IObsController obsController;
        private readonly CancellationTokenProvider tokenProvider;

        public HeroesReplayEngine(
            ILogger<HeroesReplayEngine> logger,
            AppSettings settings,
            ITwitchBot twitchBot,
            IGameManager gameManager,
            IGameData gameData,
            IReplayProvider replayProvider,
            IObsController obsController,
            CancellationTokenProvider tokenProvider
            )
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.twitchBot = twitchBot ?? throw new ArgumentNullException(nameof(twitchBot));
            this.gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
            this.replayProvider = replayProvider ?? throw new ArgumentNullException(nameof(replayProvider));
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            this.obsController = obsController ?? throw new ArgumentNullException(nameof(obsController));
        }

        public async Task RunAsync()
        {
            try
            {
                await Task.WhenAll(
                    Task.Run(SpectatorAsync, tokenProvider.Token),
                    Task.Run(TwitchBotAsync, tokenProvider.Token));
            }
            catch (Exception e)
            {
                logger.LogError(e, "There was an error in the replay consumer.");
            }
        }

        private async Task TwitchBotAsync()
        {
            await twitchBot.ConnectAsync();
        }

        private async Task SpectatorAsync()
        {
            using (obsController)
            {
                if (settings.OBS.Enabled)
                {
                    obsController.Connect();
                }

                await gameData.LoadDataAsync().ConfigureAwait(false);

                while (!tokenProvider.Token.IsCancellationRequested)
                {
                    StormReplay stormReplay = await replayProvider.TryLoadReplayAsync().ConfigureAwait(false);

                    if (stormReplay != null)
                    {
                        await gameManager.SpectateAsync(stormReplay).ConfigureAwait(false);
                    }
                }
            }
        }
    }
}