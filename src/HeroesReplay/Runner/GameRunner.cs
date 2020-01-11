using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay
{
    public sealed class GameRunner
    {
        private readonly ILogger<GameRunner> logger;
        private readonly BattleNet battleNet;
        private readonly HeroesOfTheStorm heroesOfTheStorm;
        private readonly Spectator spectator;

        public GameRunner(ILogger<GameRunner> logger, BattleNet battleNet, HeroesOfTheStorm heroesOfTheStorm, Spectator spectator)
        {
            this.logger = logger;
            this.battleNet = battleNet;
            this.heroesOfTheStorm = heroesOfTheStorm;
            this.spectator = spectator;
        }

        private void RegisterEvents()
        {
            spectator.HeroChange += OnHeroChange;
            spectator.PanelChange += OnPanelChange;
            spectator.StateChange += OnStateChange;
        }

        private void DeregisterEvents()
        {
            spectator.HeroChange -= OnHeroChange;
            spectator.PanelChange -= OnPanelChange;
            spectator.StateChange -= OnStateChange;
        }
        

        /// <summary>
        /// Starts Battle.net if it is not running.
        /// Launches the game via Battle.net
        /// Waits until the main HeroesOfTheStorm_x64.exe is finished
        /// Calls the HeroSwitcher_x64.exe which detects which game client needs to launch that supports the replay file.
        /// </summary>
        public async Task RunAsync(StormReplay stormReplay, CancellationToken token = default)
        {
            try
            {
                RegisterEvents();

                heroesOfTheStorm.KillGame();
                await heroesOfTheStorm.SetGameVariables();

                var started = await battleNet.WaitForBattleNetAsync(token);

                if (!started)
                {
                    throw new Exception("BattleNet process was not found, so cannot attempt to start the game.");
                }

                var launched = await battleNet.WaitForGameLaunchedAsync(token);

                if (!launched)
                {
                    throw new Exception("Game process was not found launched.");
                }

                var selected = await heroesOfTheStorm.WaitForSelectedReplayAsync(stormReplay, token);

                if (!selected)
                {
                    throw new Exception("Game process version not found matching replay version: " + stormReplay.Replay.ReplayVersion);
                }

                var loading = await heroesOfTheStorm.WaitForMapLoadingAsync(stormReplay, token);

                if (!loading)
                {
                    throw new Exception("Game process not found in loading state.");
                }

                await spectator.SpectateAsync(stormReplay, token);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in running replay: " + stormReplay.FilePath);
            }
            finally
            {
                DeregisterEvents();

                heroesOfTheStorm.KillGame();
            }
        }

        public void SendToggleChat() => heroesOfTheStorm.SendToggleChat();

        public void SendToggleTime() => heroesOfTheStorm.SendToggleTime();

        public void SendTogglePause() => heroesOfTheStorm.SendTogglePause();

        public void SendToggleControls() => heroesOfTheStorm.SendToggleControls();

        public void SendToggleBottomConsole() => heroesOfTheStorm.SendToggleBottomConsole();

        public void SendToggleInfoPanel() => heroesOfTheStorm.SendToggleInfoPanel();

        private void OnStateChange(object sender, GameEventArgs<StateDelta> e)
        {
            if (e.Data.Previous != State.StartOfGame || e.Data.Current != State.Running) return;

            logger.LogInformation($"StormReplay started, zooming out and disabling chat. Thread: {Thread.CurrentThread.ManagedThreadId}");

            // heroesOfTheStorm.SendToggleZoom(); // Max Zoom

            // heroesOfTheStorm.SendToggleChat(); // Hide Chat
        }

        private void OnHeroChange(object sender, GameEventArgs<Player> e)
        {
            for (int index = 0; index < e.StormReplay.Replay.Players.Length; index++)
            {
                if (e.StormReplay.Replay.Players[index] == e.Data)
                {
                    logger.LogInformation($"Focusing {e.Data.Character}. Reason: {e.Message}. Thread: {Thread.CurrentThread.ManagedThreadId}");

                    heroesOfTheStorm.SendFocusHero(index);
                }
            }
        }

        private void OnPanelChange(object sender, GameEventArgs<Panel> e)
        {
            logger.LogInformation($"Switching Panel: {e.Data}. Thread: {Thread.CurrentThread.ManagedThreadId}");

            heroesOfTheStorm.SendPanelChange(e.Data);
        }
    }
}
