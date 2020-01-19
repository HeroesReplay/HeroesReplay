using System;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Spectator
{
    public sealed class StormReplayRunner
    {
        private readonly ILogger<StormReplayRunner> logger;
        private readonly BattleNet battleNet;
        private readonly HeroesOfTheStorm heroesOfTheStorm;
        private readonly Spectator spectator;

        public StormReplayRunner(ILogger<StormReplayRunner> logger, BattleNet battleNet, HeroesOfTheStorm heroesOfTheStorm, Spectator spectator)
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

        private async Task RunAsync(StormReplay stormReplay)
        {
            await spectator.SpectateAsync(stormReplay);
        }

        private async Task LaunchAndRunAsync(StormReplay stormReplay)
        {
            try
            {
                heroesOfTheStorm.KillGame();
                await heroesOfTheStorm.SetVariablesAsync();

                var started = await battleNet.WaitForBattleNetAsync();

                if (!started)
                {
                    throw new Exception("BattleNet process was not found, so cannot attempt to start the game.");
                }

                var launched = await battleNet.WaitForGameLaunchedAsync();

                if (!launched)
                {
                    throw new Exception("Game process was not found launched.");
                }

                var selected = await heroesOfTheStorm.WaitForSelectedReplayAsync(stormReplay);

                if (!selected)
                {
                    throw new Exception("Game process version not found matching replay version: " + stormReplay.Replay.ReplayVersion);
                }

                var loading = await heroesOfTheStorm.WaitForMapLoadingAsync(stormReplay);

                if (!loading)
                {
                    throw new Exception("Game process not found in loading state.");
                }

                await spectator.SpectateAsync(stormReplay);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in running replay: " + stormReplay.FilePath);
            }
            finally
            {
                heroesOfTheStorm.KillGame();
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

                if (launch)
                {
                    await LaunchAndRunAsync(stormReplay);
                }
                else
                {
                    await RunAsync(stormReplay);
                }
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

        private void OnStateChange(object sender, GameEventArgs<StateDelta> e)
        {
            if (e.Data.Previous == State.StartOfGame && e.Data.Current == State.Running && e.Timer < TimeSpan.FromSeconds(10))
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

        private void OnPanelChange(object sender, GameEventArgs<Panel> e)
        {
            heroesOfTheStorm.SendPanelChange(e.Data);
        }
    }
}
