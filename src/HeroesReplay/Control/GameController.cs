using Heroes.ReplayParser;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay
{
    /// <summary>
    /// The Game Controller does not manage the state. It simply sends commands to the game client.
    /// The Game Spectator manages state AND raises events to the game controller so that the controller can issue commands to the game client.
    /// </summary>
    public class GameController : IDisposable
    {
        private readonly GameSpectator spectator;

        public GameController(GameSpectator spectator)
        {
            this.spectator = spectator;

            spectator.HeroChange += OnHeroChange;
            spectator.PanelChange += OnPanelChange;
            spectator.StateChange += OnStateChange;
        }

        public async Task RunAsync(Game game, CancellationToken token)
        {
            if (Win32.TryKillGame())
            {
                await Task.Delay(5000, token);
            }

            if (await Win32.TryLaunchAsync(game, token))
            {
                await spectator.RunAsync(game, token);
            }
        }

        public void SendToggleChat() => Win32.SendToggleChat();

        public void SendToggleTime() => Win32.SendToggleTime();

        public void SendTogglePause()
        {
            Win32.SendTogglePause();

            if (spectator.GameState == GameState.Running)
                spectator.GameState = GameState.Paused;

            if (spectator.GameState == GameState.Paused)
                spectator.GameState = GameState.Running;
        }

        public void SendToggleControls() => Win32.SendToggleControls();

        public void SendToggleBottomConsole() => Win32.SendToggleBottomConsole();

        public void SendToggleInfoPanel() => Win32.SendToggleInfoPanel();

        private void OnHeroChange(object sender, GameEvent<Player> e) => SendFocusHero(e);

        private void OnPanelChange(object sender, GameEvent<GamePanel> e) => SendPanelChange(e);

        private void OnStateChange(object sender, GameEvent<StateDelta> e)
        {
            if (e.Data.Previous == GameState.Loading && e.Data.Current == GameState.Running)
            {
                Console.WriteLine($"Game started, zooming out and disabling chat.");

                Win32.SendToggleZoom(); // Max Zoom

                Win32.SendToggleChat(); // Hide Chat
            }
        }

        private void SendFocusHero(GameEvent<Player> e)
        {
            Console.WriteLine($"Focusing {e.Data.Character}. Reason: {e.Message}");

            for (int index = 0; index < 10; index++)
            {
                if (e.Game.Replay.Players[index] == e.Data)
                {
                    Win32.SendFocusHero(index);
                }
            }
        }

        private void SendPanelChange(GameEvent<GamePanel> e)
        {
            Console.WriteLine($"Switching Panel: {e.Data}");

            Win32.SendPanelChange(e.Data);
        }

        public void Dispose()
        {
            spectator.HeroChange += OnHeroChange;
            spectator.PanelChange += OnPanelChange;
            spectator.StateChange += OnStateChange;
        }
    }
}
