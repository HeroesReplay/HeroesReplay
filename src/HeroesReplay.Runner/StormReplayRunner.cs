using System;
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
                if (launch)
                {
                    heroesOfTheStorm.TryKillGame();
                }

                if (!heroesOfTheStorm.IsRunning)
                {
                    await heroesOfTheStorm.ConfigureClientAsync();

                    if (await battleNet.WaitForBattleNetAsync())
                    {
                        if (await battleNet.WaitForGameLaunchedAsync())
                        {
                            if (await heroesOfTheStorm.WaitForSelectedReplayAsync(stormReplay))
                            {
                                if (await heroesOfTheStorm.WaitForMapLoadingAsync(stormReplay))
                                {
                                    await stormReplaySpectator.SpectateAsync(stormReplay);
                                }
                                else
                                {
                                    throw new Exception("Game process not found in loading gameState.");
                                }
                            }
                            else
                            {
                                throw new Exception("Game process version not found matching replay version: " + stormReplay.Replay.ReplayVersion);
                            }
                        }
                        else
                        {
                            throw new Exception("Game process was not found launched.");
                        }
                    }
                    else
                    {
                        throw new Exception("BattleNet process was not found, so cannot attempt to start the game.");
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in running replay: " + stormReplay.Path);
            }
            finally
            {
                heroesOfTheStorm.TryKillGame();
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
            if (e.Data.Previous == GameState.StartOfGame && e.Data.Current == GameState.Running && e.Timer <  TimeSpan.FromSeconds(35))
            {   
                heroesOfTheStorm.SendToggleChat();
                Thread.Sleep(TimeSpan.FromSeconds(5));

                heroesOfTheStorm.SendToggleControls();
                Thread.Sleep(TimeSpan.FromSeconds(5));

                heroesOfTheStorm.SendToggleZoom();
                Thread.Sleep(TimeSpan.FromSeconds(5));
            }
        }

        private void OnHeroChange(object sender, GameEventArgs<Player> e)
        {
            for (int index = 0; index < e.StormReplay.Replay.Players.Length; index++)
            {
                if (e.StormReplay.Replay.Players[index] == e.Data)
                {
                    heroesOfTheStorm.SendFocusHero(index);
                }
            }
        }

        private void OnPanelChange(object sender, GameEventArgs<GamePanel> e)
        {
            heroesOfTheStorm.SendPanelChange((int)e.Data);
        }
    }
}
