using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Processes;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectator;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Runner
{
    public sealed class ReplayRunner
    {
        private readonly ILogger<ReplayRunner> logger;
        private readonly HeroesOfTheStorm process;
        private readonly StormReplaySpectator spectator;
        private readonly ReplayHelper replayHelper;

        private bool zoomout = false;

        public ReplayRunner(ILogger<ReplayRunner> logger, HeroesOfTheStorm process, StormReplaySpectator spectator, ReplayHelper replayHelper)
        {
            this.logger = logger;
            this.process = process;
            this.spectator = spectator;
            this.replayHelper = replayHelper;
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
                zoomout = false;
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
            process.SendFocusHero(replayHelper.GetPlayerIndex(e.StormReplay.Replay, e.Data.Current.Player));

            // A hero must first be selected before doing maximum zoom (weird client behaviour that requires this)
            if (zoomout == false)
            {
                process.SendToggleZoom();
                zoomout = true;
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
