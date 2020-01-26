using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Processes;
using HeroesReplay.Shared;
using HeroesReplay.Spectator;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Runner
{
    public sealed class StormReplayRunner
    {
        private readonly ILogger<StormReplayRunner> logger;
        private readonly BattleNet battleNet;
        private readonly HeroesOfTheStorm heroesOfTheStorm;
        private readonly StormReplaySpectator stormReplaySpectator;

        public StormReplayRunner(ILogger<StormReplayRunner> logger, BattleNet battleNet, HeroesOfTheStorm heroesOfTheStorm, StormReplaySpectator stormReplaySpectator)
        {
            this.logger = logger;
            this.battleNet = battleNet;
            this.heroesOfTheStorm = heroesOfTheStorm;
            this.stormReplaySpectator = stormReplaySpectator;
        }

        private void RegisterEvents()
        {
            stormReplaySpectator.HeroChange += OnHeroChange;
            stormReplaySpectator.PanelChange += OnPanelChange;
            stormReplaySpectator.StateChange += OnStateChange;
        }

        private void DeregisterEvents()
        {
            stormReplaySpectator.HeroChange -= OnHeroChange;
            stormReplaySpectator.PanelChange -= OnPanelChange;
            stormReplaySpectator.StateChange -= OnStateChange;
        }

        private async Task RunAsync(StormReplay stormReplay, bool launch)
        {
            try
            {
                switch (launch)
                {
                    case false when heroesOfTheStorm.IsRunning:
                    {
                        await stormReplaySpectator.SpectateAsync(stormReplay);
                        break;
                    }
                    case false when !heroesOfTheStorm.IsRunning:
                    {
                        await LaunchGame(stormReplay);
                        await stormReplaySpectator.SpectateAsync(stormReplay);
                        break;
                    }
                    case true when heroesOfTheStorm.IsRunning:
                    {
                        await heroesOfTheStorm.TryKillGameAsync();
                        await heroesOfTheStorm.ConfigureClientAsync();
                        await LaunchGame(stormReplay);
                        await stormReplaySpectator.SpectateAsync(stormReplay);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in running replay: " + stormReplay.Path);
            }
            finally
            {
                await heroesOfTheStorm.TryKillGameAsync();
            }
        }

        private async Task LaunchGame(StormReplay stormReplay)
        {
            if (!await battleNet.WaitForBattleNetAsync())
            {
                throw new Exception("BattleNet process was not found, so cannot attempt to start the game.");
            }

            if (!await battleNet.WaitForGameLaunchedAsync())
            {
                throw new Exception("Game process was not found after attempting to launch the game.");
            }

            if (!await heroesOfTheStorm.WaitForSelectedReplayAsync(stormReplay))
            {
                throw new Exception("Game process version not found matching replay version: " +
                                    stormReplay.Replay.ReplayVersion);
            }

            if (!await heroesOfTheStorm.WaitForMapLoadingAsync(stormReplay))
            {
                throw new Exception("Map loading state was not detected after selecting: " + stormReplay.Path);
            }
        }

        /// <summary>
        /// Starts Battle.net if it is not running.
        /// Launches the game via Battle.net
        /// Waits until the main HeroesOfTheStorm_x64.exe is finished
        /// Calls the HeroSwitcher_x64.exe which detects which game client needs to launch that supports the replay file.
        /// </summary>
        public async Task ReplayAsync(StormReplay stormReplay, bool launch)
        {
            try
            {
                RegisterEvents();

                await RunAsync(stormReplay, launch);
            }
            finally
            {
                DeregisterEvents();
            }
        }

        public void SendToggleChat() => heroesOfTheStorm.SendToggleChat();

        public void SendToggleTime() => heroesOfTheStorm.SendToggleTime();

        public void SendTogglePause() => heroesOfTheStorm.SendTogglePause();

        public void SendToggleControls() => heroesOfTheStorm.SendToggleControls();

        public void SendToggleBottomConsole() => heroesOfTheStorm.SendToggleBottomConsole();

        public void SendToggleInfoPanel() => heroesOfTheStorm.SendToggleInfoPanel();

        private void OnStateChange(object sender, GameEventArgs<GameStateDelta> e)
        {
            if (e.Data.Previous == GameState.StartOfGame && e.Data.Current == GameState.Running)
            {
                heroesOfTheStorm.SendToggleChat();
                Thread.Sleep(TimeSpan.FromSeconds(2));
                
                heroesOfTheStorm.SendToggleControls();
                Thread.Sleep(TimeSpan.FromSeconds(2));
            }
        }

        private void OnHeroChange(object sender, GameEventArgs<StormPlayerDelta> e)
        {   
            if (e.Data.Previous == null && e.Timer < TimeSpan.FromSeconds(30))
            {
                // This is the first hero of the game being selected, lets configure and adjust camera

                heroesOfTheStorm.SendFocusHero(Array.IndexOf(e.StormReplay.Replay.Players, e.Data.Current.Player));
                Thread.Sleep(TimeSpan.FromSeconds(2));

                // heroesOfTheStorm.SendFollow();
                // Thread.Sleep(TimeSpan.FromSeconds(2));

                heroesOfTheStorm.SendToggleZoom();
                Thread.Sleep(TimeSpan.FromSeconds(2));
            }
            else
            {
                heroesOfTheStorm.SendFocusHero(Array.IndexOf(e.StormReplay.Replay.Players, e.Data.Current.Player));
            }
        }

        private void OnPanelChange(object sender, GameEventArgs<GamePanel> e)
        {
            heroesOfTheStorm.SendPanelChange((int)e.Data);
        }
    }
}
