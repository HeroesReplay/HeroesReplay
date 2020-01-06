using Heroes.ReplayParser;

using System;
using System.Diagnostics;

namespace HeroesReplay
{
    /// <summary>
    /// The Game Controller does not manage the state. It simply sends commands to the game client.
    /// The Game Spectator manages state AND raises events to the game controller so that the controller can issue commands to the game client.
    /// </summary>
    public class GameController : IDisposable
    {
        public event EventHandler<EventData<Player>> HeroChange;
        public event EventHandler<EventData<GamePanel>> PanelChange;
        public event EventHandler<EventData<StateDelta>> StateChanged;

        public event EventHandler<EventArgs> TogglePause;
        public event EventHandler<EventArgs> ToggleZoom;
        public event EventHandler<EventArgs> ToggleDetails;
        public event EventHandler<EventArgs> ToggleConsole;

        private readonly GameSpectator spectator;
        private readonly Game game;

        public GameController(Game game)
        {
            this.game = game;
            spectator = new GameSpectator(game);
        }

        public bool TryLaunchGameProcess() => Win32.TryLaunchGame(game);

        public bool TryKillGameProcess() => Win32.TryKillGame();

        public void Stop()
        {
            UnRegisterListeners();
            spectator.Stop();
        }

        public void Start()
        {
            UnRegisterListeners();
            RegisterListeners();
            spectator.Start();
        }

        public void SendToggleChat() => Win32.SendToggleChat();

        public void SendToggleTime() => Win32.SendToggleTime();

        public void SendTogglePause() => Win32.SendTogglePause();

        public void SendToggleControls() => Win32.SendToggleControls();

        public void SendToggleBottomConsole() => Win32.SendToggleBottomConsole();

        public void SendToggleInfoPanel() => Win32.SendToggleInfoPanel();

        public void Dispose() => spectator?.Dispose();

        private void RegisterListeners()
        {
            spectator.StateChange += StateChanged;

            spectator.HeroChange += OnHeroChange;
            spectator.PanelChange += OnPanelChange;
            spectator.StateChange += OnStateChanged;
        }

        private void UnRegisterListeners()
        {
            spectator.StateChange -= StateChanged;

            spectator.HeroChange -= OnHeroChange;
            spectator.PanelChange -= OnPanelChange;
            spectator.StateChange -= OnStateChanged;
        }

        private void OnHeroChange(object sender, EventData<Player> e) => SendFocusHero(e);

        private void OnPanelChange(object sender, EventData<GamePanel> e) => SendPanelChange(e);

        private void OnStateChanged(object sender, EventData<StateDelta> e)
        {
            if (e.Data.Previous == GameState.Loading && e.Data.Current == GameState.Running)
            {
                Win32.SendToggleZoom(); // Max Zoom
                Win32.SendToggleChat(); // Hide Chat
            }
        }

        private void SendFocusHero(EventData<Player> e)
        {
            if (spectator.GameState == GameState.Paused) return;

            Console.WriteLine($"Focusing {e.Data.Character}. Reason: {e.Message}");

            for (int index = 0; index < 10; index++)
            {
                if (e.Replay.Players[index] == e.Data)
                {
                    Win32.SendFocusHero(index);
                }
            }
        }

        private void SendPanelChange(EventData<GamePanel> e) => Win32.SendPanelChange(e.Data);
    }
}
