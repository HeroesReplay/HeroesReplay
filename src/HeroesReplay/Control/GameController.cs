using Heroes.ReplayParser;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay
{
    /// <summary>
    /// The Game Controller does not manage the state. It simply sends commands to the game client.
    /// The Game Spectator manages state AND raises events to the game controller so that the controller can issue commands to the game client.
    /// </summary>
    public sealed class GameController : IDisposable
    {
        private readonly ILogger<GameController> logger;
        private readonly GameWrapper wrapper;
        private readonly GameSpectator spectator;

        public GameController(ILogger<GameController> logger, GameWrapper wrapper, GameSpectator spectator)
        {
            this.logger = logger;
            this.wrapper = wrapper;
            this.spectator = spectator;

            spectator.HeroChange += OnHeroChange;
            spectator.PanelChange += OnPanelChange;
            spectator.StateChange += OnStateChange;
        }

        public async Task RunAsync(Game game, CancellationToken token)
        {
            try
            {
                if (await wrapper.TryLaunchAsync(game, token))
                {
                    await spectator.RunAsync(game, token);

                    wrapper.TryKillGame();
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, e.Message);
            }
        }

        public void SendToggleChat() => wrapper.SendToggleChat();

        public void SendToggleTime() => wrapper.SendToggleTime();

        public void SendTogglePause()
        {
            wrapper.SendTogglePause();

            if (spectator.State == GameState.Running)
                spectator.State = GameState.Paused;

            if (spectator.State == GameState.Paused)
                spectator.State = GameState.Running;
        }

        public void SendToggleControls() => wrapper.SendToggleControls();

        public void SendToggleBottomConsole() => wrapper.SendToggleBottomConsole();

        public void SendToggleInfoPanel() => wrapper.SendToggleInfoPanel();

        private void OnHeroChange(object sender, GameEventArgs<Player> e) => SendFocusHero(e);

        private void OnPanelChange(object sender, GameEventArgs<GamePanel> e) => SendPanelChange(e);

        private void OnStateChange(object sender, GameEventArgs<StateDelta> e)
        {
            if (e.Data.Previous == GameState.StartOfGame && e.Data.Current == GameState.Running)
            {
                logger.LogInformation($"Game started, zooming out and disabling chat. Thread: {Thread.CurrentThread.ManagedThreadId}");

                wrapper.SendToggleZoom(); // Max Zoom

                wrapper.SendToggleChat(); // Hide Chat
            }
        }

        private void SendFocusHero(GameEventArgs<Player> e)
        {
            for (int index = 0; index < 10; index++)
            {
                if (e.Game.Replay.Players[index] == e.Data)
                {
                    logger.LogInformation($"Focusing {e.Data.Character}. Reason: {e.Message}. Thread: {Thread.CurrentThread.ManagedThreadId}");

                    wrapper.SendFocusHero(index);
                }
            }
        }

        private void SendPanelChange(GameEventArgs<GamePanel> e)
        {
            logger.LogInformation($"Switching Panel: {e.Data}. Thread: {Thread.CurrentThread.ManagedThreadId}");

            wrapper.SendPanelChange(e.Data);
        }

        public void Dispose()
        {
            spectator.HeroChange += OnHeroChange;
            spectator.PanelChange += OnPanelChange;
            spectator.StateChange += OnStateChange;
        }
    }
}
