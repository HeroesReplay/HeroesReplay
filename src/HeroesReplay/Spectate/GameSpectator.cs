using Heroes.ReplayParser;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;

using ThreadState = System.Threading.ThreadState;

namespace HeroesReplay
{
    /// <summary>
    /// The Game spectator manages the state (paused, running, start, end) and fires events when it detects these changes.
    /// The Game spectator does NOT send commands to the game, it only raises events to which the game controller should respond with.
    /// </summary>
    public class GameSpectator
    {
        public Panel Current
        {
            get => current;
            set
            {
                if (current != value)
                {
                    PanelChange?.Invoke(this, new EventData<Panel>(Replay, value, "Change"));
                }

                current = value;
            }
        }

        public bool IsRunning => stopwatch.IsRunning;

        public SpectatorState State
        {
            get
            {
                if (stopwatch.Elapsed >= Replay.ReplayLength && stopwatch.IsRunning) return SpectatorState.EndOfGame;
                if (stopwatch.Elapsed == TimeSpan.Zero && !stopwatch.IsRunning) return SpectatorState.StartOfGame;
                if (stopwatch.IsRunning) return SpectatorState.Running;
                return SpectatorState.Paused;
            }
            set
            {
                if (State != value)
                {
                    if (value == SpectatorState.EndOfGame)
                    {
                        Ended?.Invoke(this, new EventData<TimeSpan>(Replay, stopwatch.Elapsed, value.ToString()));
                    }

                    if (value == SpectatorState.StartOfGame)
                    {
                        stopwatch.Start();
                        Started?.Invoke(this, new EventData<TimeSpan>(Replay, stopwatch.Elapsed, $"started at {stopwatch.Elapsed}"));
                    }

                    if (value == SpectatorState.Paused)
                    {
                        stopwatch.Stop();
                        Paused?.Invoke(this, new EventData<TimeSpan>(Replay, stopwatch.Elapsed, $"paused at {stopwatch.Elapsed}."));
                    }
                }

                State = value;
            }
        }

        public TimeSpan PanelWait { get; set; } = TimeSpan.FromSeconds(10);

        public event EventHandler<EventData<Player>> HeroChange;
        public event EventHandler<EventData<Panel>> PanelChange;
        public event EventHandler<EventData<TimeSpan>> Ended;
        public event EventHandler<EventData<TimeSpan>> Started;
        public event EventHandler<EventData<TimeSpan>> Paused;

        public TimeSpan Timer => stopwatch.Elapsed;

        private List<Player> bluePlayers = new List<Player>();
        private List<Player> redPlayers = new List<Player>();

        private Stopwatch stopwatch = new Stopwatch();

        private Thread panelThread;
        private Thread playerThread;
        private Thread stateThread;

        private Game game;
        private ViewContextBuilder view;

        public static readonly List<GameMode> SupportedModes = new List<GameMode>() { GameMode.Brawl, GameMode.HeroLeague, GameMode.QuickMatch, GameMode.UnrankedDraft };
        private readonly List<Panel> Panels = Enum.GetValues(typeof(Panel)).Cast<Panel>().ToList();
        private Panel current;

        private Replay Replay => game.Replay;

        public GameSpectator(Game game)
        {
            this.game = game;
            view = new ViewContextBuilder(stopwatch, Replay);
            stateThread = new Thread(StateLoop);
            playerThread = new Thread(PlayerLoop);
            panelThread = new Thread(PanelLoop);
        }

        public void Start()
        {
            if (playerThread.ThreadState == ThreadState.Unstarted)
                playerThread.Start();

            if (panelThread.ThreadState == ThreadState.Unstarted)
                panelThread.Start();

            if (stateThread.ThreadState == ThreadState.Unstarted)
                stateThread.Start();
        }

        public void Stop()
        {
            State = SpectatorState.Paused;
        }

        public void Reset()
        {
            stopwatch.Stop();
            stopwatch.Reset();
            bluePlayers.Clear();
            redPlayers.Clear();
            bluePlayers.AddRange(Replay.Players.Take(5));
            redPlayers.AddRange(Replay.Players.TakeLast(5));
        }

        public void Dispose()
        {
            bluePlayers = null;
            redPlayers = null;
            HeroChange = null;
            PanelChange = null;
            stopwatch = null;
            game = null;

            playerThread = null;
            panelThread = null;
            stateThread = null;
        }

        private void PlayerLoop()
        {
            while (playerThread != null)
            {
                while (stopwatch.IsRunning)
                {
                    try
                    {
                        var context = view.TheNext(10).Seconds;

                        if (context.Deaths.Any())
                        {
                            foreach (var unit in context.Deaths)
                            {
                                HeroChange?.Invoke(this, new EventData<Player>(Replay, unit.PlayerKilledBy ?? unit.PlayerControlledBy, "PlayerDeaths"));
                                Thread.Sleep(stopwatch.Elapsed - unit.TimeSpanDied.Value);
                            }
                        }
                        else if (context.Objectives.Any())
                        {
                            foreach (var unit in context.Objectives)
                            {
                                HeroChange?.Invoke(this, new EventData<Player>(Replay, unit.PlayerKilledBy ?? unit.PlayerControlledBy, "PlayersObjectives"));
                                Thread.Sleep(stopwatch.Elapsed - unit.TimeSpanDied.Value);
                            }
                        }
                        else if (context.Structures.Any())
                        {
                            foreach (var unit in context.Structures)
                            {
                                HeroChange?.Invoke(this, new EventData<Player>(Replay, unit.PlayerKilledBy ?? unit.PlayerControlledBy, "PlayersStructures"));
                                Thread.Sleep(stopwatch.Elapsed - unit.TimeSpanDied.Value);
                            }
                        }
                        else if (context.Alive.Any())
                        {
                            foreach (var player in context.Alive)
                            {
                                HeroChange?.Invoke(this, new EventData<Player>(Replay, player, "PlayersAlive"));
                                Thread.Sleep(TimeSpan.FromSeconds(10.0 / context.Alive.Count()));
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }

                Thread.Sleep(100);
            }
        }

        private void PanelLoop()
        {
            while (panelThread != null)
            {
                while (stopwatch.IsRunning)
                {
                    try
                    {
                        var context = view.TheNext(5).Seconds;

                        if (context.Talents.Any())
                        {
                            Current = Panel.Talents;
                        }
                        else if (context.TeamObjectives.Any())
                        {
                            Current = Panel.CarriedObjectives;
                        }
                        else if (context.Deaths.Any())
                        {
                            Current = Panel.KillsDeathsAssists;
                        }
                        else
                        {
                            // Cycle
                            int next = Panels.IndexOf(Current) + 1;
                            Current = next >= Panels.Count ? Panels.First() : Panels[next];
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }

                    Thread.Sleep(PanelWait);
                }

                Thread.Sleep(200);
            }
        }

        private void StateLoop()
        {
            while (stateThread != null)
            {
                try
                {
                    if (stopwatch.Elapsed >= Replay.ReplayLength)
                    {
                        State = SpectatorState.EndOfGame;
                    }

                    if (State == SpectatorState.Loading)
                    {
                        if (TryDetectGame())
                        {
                            State = SpectatorState.StartOfGame;
                        }
                    }

                    if (State == SpectatorState.Paused)
                    {
                        // try detect unpaused
                        if (TryIsRunning(out var running))
                        {
                            if(running) State = SpectatorState.Running;

                        }
                    }

                    if (State == SpectatorState.Running)
                    {
                        // try detect pause
                        if (TryIsPaused(out var paused))
                        {
                            if (paused) State = SpectatorState.Paused;                                
                        }
                    }

                    // Start, Paused
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }

                LogTimerToConsole();

                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }

        private void LogTimerToConsole()
        {
            if ((int)Timer.TotalSeconds % 2 == 0)
            {
                Console.WriteLine("Timer: " + Timer);
            }
        }

        private bool TryIsRunning(out bool running)
        {
            running = false;

            try
            {
                if (Win32.TryGetScreenshot(out Bitmap screenshot))
                {
                    // TODO: if image contains [Pause Icon] return true
                }

                return false;
            }
            catch (Exception e)
            {

            }

            return false;
        }

        private bool TryIsPaused(out bool paused)
        {
            paused = false;

            try
            {
                if (Win32.TryGetScreenshot(out Bitmap screenshot))
                {
                    // TODO: if image contains [Play Icon] return true
                }

                return false;
            }
            catch (Exception e)
            {

            }

            return false;
        }

        private bool TryDetectGame()
        {
            try
            {
                if (Win32.TryGetScreenshot(out Bitmap screenshot))
                {
                    // TODO: if image contains [Pause Icon] return true
                }

                return false;
            }
            catch (Exception e)
            {

            }

            return false;
        }

    }
}