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
        public event EventHandler<EventData<Panel>> PanelChange;

        public event EventHandler<EventData<TimeSpan>> GameEnded;
        public event EventHandler<EventData<TimeSpan>> GamePaused;
        public event EventHandler<EventData<TimeSpan>> GameStarted;

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

        public bool TryLaunchGameProcess()
        {
            return Win32.TryLaunchGame(game);
        }

        public bool TryKillGameProcess()
        {
            try
            {
                Process.GetProcessesByName("HeroesOfTheStorm_x64")[0].Kill();
                return true;
            }
            catch (Exception e)
            {

            }

            return false;
        }

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
            spectator.Started += GameStarted;
            spectator.Ended += GameEnded;
            spectator.Paused += GameStarted;

            spectator.HeroChange += OnHeroChange;
            spectator.PanelChange += OnPanelChange;
            spectator.Paused += OnGameStarted;
        }

        private void UnRegisterListeners()
        {
            spectator.Started -= GameStarted;
            spectator.Paused -= GamePaused;
            spectator.Ended -= GameEnded;

            spectator.HeroChange -= OnHeroChange;
            spectator.PanelChange -= OnPanelChange;
            spectator.Paused -= OnGameStarted;
        }

        private void OnHeroChange(object sender, EventData<Player> e) => SendFocusHero(e);

        private void OnPanelChange(object sender, EventData<Panel> e) => SendPanelChange(e);

        private void OnGameStarted(object sender, EventData<TimeSpan> e)
        {
            Win32.SendToggleZoom(); // Max Zoom
            Win32.SendToggleChat(); // Hide Chat
        }

        private void SendFocusHero(EventData<Player> e)
        {
            for (int index = 0; index < 10; index++)
            {
                if (e.Replay.Players[index] == e.Data)
                {
                    Win32.SendFocusHero(index);
                }
            }
        }

        private void SendPanelChange(EventData<Panel> e) => Win32.SendPanelChange(e.Data);
    }
}
