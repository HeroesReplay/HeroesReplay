using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Processes;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectator;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Runner
{
    public sealed class StormReplayRunner
    {
        private readonly ILogger<StormReplayRunner> logger;
        private readonly HeroesOfTheStorm process;
        private readonly StormReplaySpectator spectator;

        public StormReplayRunner(ILogger<StormReplayRunner> logger, HeroesOfTheStorm process, StormReplaySpectator spectator)
        {
            this.logger = logger;
            this.process = process;
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

        private async Task RunAsync(StormReplay stormReplay, bool launch)
        {
            try
            {
                if (launch == false && process.IsRunning)
                {
                    await spectator.SpectateAsync(stormReplay);
                }
                else if (launch == false && !process.IsRunning)
                {
                    await process.ConfigureClientAsync();
                    await LaunchGame(stormReplay);
                    await spectator.SpectateAsync(stormReplay);
                }
                else if (launch && process.IsRunning)
                {
                    await process.TryKillGameAsync();
                    await process.ConfigureClientAsync();
                    await LaunchGame(stormReplay);
                    await spectator.SpectateAsync(stormReplay);
                }
                else if (launch && !process.IsRunning)
                {
                    await process.ConfigureClientAsync();
                    await LaunchGame(stormReplay);
                    await spectator.SpectateAsync(stormReplay);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Error in running replay: {stormReplay.Path}");
            }
            finally
            {
                await process.TryKillGameAsync();
            }
        }

        private async Task LaunchGame(StormReplay stormReplay)
        {
            if (!await process.LaunchSelectedReplayAsync(stormReplay))
            {
                throw new Exception($"Game process version not found matching replay version: {stormReplay.Replay.ReplayVersion}");
            }

            if (!await process.WaitForMapLoadingAsync(stormReplay))
            {
                throw new Exception($"Map loading state was not detected after selecting: {stormReplay.Path}");
            }
        }

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

        public void SendToggleChat() => process.SendToggleChat();

        public void SendToggleTime() => process.SendToggleTime();

        public void SendTogglePause() => process.SendTogglePause();

        public void SendToggleControls() => process.SendToggleControls();

        public void SendToggleBottomConsole() => process.SendToggleBottomConsole();

        public void SendToggleInfoPanel() => process.SendToggleInfoPanel();

        public void SendFocusHero(int index) => process.SendFocusHero(index);

        public void SendPanelChange(int index) => process.SendPanelChange(index);

        public void SendToggleZoom(int index) => process.SendToggleZoom();


        private void OnStateChange(object sender, GameEventArgs<Delta<StormState>> e)
        {
            if (e.Data.Previous.IsStart() && e.Data.Current.IsRunning() && e.Timer < TimeSpan.FromSeconds(30))
            {
                process.SendToggleChat();
                process.SendToggleControls();
            }
        }

        private void OnHeroChange(object sender, GameEventArgs<Delta<StormPlayer>> e)
        {
            process.SendFocusHero(e.StormReplay.GetPlayerIndex(e.Data.Current.Player));

            bool firstHeroSelected = e.Data.Previous == null && e.Timer < TimeSpan.FromMinutes(1);

            if (firstHeroSelected)
            {
                process.SendToggleZoom();
            }
        }

        private void OnPanelChange(object sender, GameEventArgs<Delta<GamePanel?>> e)
        {
            if (e.Data.Current.HasValue)
            {
                process.SendPanelChange((int)e.Data.Current);
            }
            else if (e.Data.Previous.HasValue)
            {
                process.SendPanelChange((int)e.Data.Previous);
            }
        }
    }
}
