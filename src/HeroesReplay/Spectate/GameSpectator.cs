using Heroes.ReplayParser;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        
        public SpectatorState State
        {
            get => state;
            set
            {
                if (state != value)
                {
                    if (value == SpectatorState.EndOfGame)
                    {
                        Ended?.Invoke(this, new EventData<TimeSpan>(Replay, stopwatch.Elapsed, value.ToString()));
                    }

                    if (value == SpectatorState.Running)
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

                state = value;
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

        private SpectatorState state;
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
                            // TODO: better logic

                            foreach (var unit in context.Deaths)
                            {
                                var reason =  unit.PlayerKilledBy == unit.PlayerControlledBy ? "Suicide" : (unit.PlayerKilledBy != null ? "Death" : "Environment");
                                HeroChange?.Invoke(this, new EventData<Player>(Replay, unit.PlayerKilledBy ?? unit.PlayerControlledBy, reason));
                                Thread.Sleep(TimeSpan.FromSeconds(3)); 
                            }
                        }
                        else if (context.Objectives.Any())
                        {
                            // TODO: better logic

                            foreach (var unit in context.Objectives)
                            {
                                HeroChange?.Invoke(this, new EventData<Player>(Replay, unit.PlayerKilledBy ?? unit.PlayerControlledBy, "Objectives"));
                                Thread.Sleep(1000);
                            }
                        }
                        else if (context.Structures.Any())
                        {
                            // TODO: better logic

                            foreach (var unit in context.Structures)
                            {
                                HeroChange?.Invoke(this, new EventData<Player>(Replay, unit.PlayerKilledBy ?? unit.PlayerControlledBy, "Structures"));
                                Thread.Sleep(5000);
                            }
                        }
                        else if (context.Alive.Any())
                        {
                            // TODO: better logic

                            // Give every alive player equal view time?
                            foreach (var player in context.Alive)
                            {
                                HeroChange?.Invoke(this, new EventData<Player>(Replay, player, "Alive"));
                                Thread.Sleep(TimeSpan.FromSeconds(10.0 / context.Alive.Count()));
                            }

                            // Give 5 seconds to a random alive player
                            //HeroChange?.Invoke(this, new EventData<Player>(Replay, context.Alive.PickRandomPlayer(), "Alive");
                            //Thread.Sleep(5000);
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
                LogTimerToConsole();

                try
                {
                    if (State == SpectatorState.Loading)
                    {
                        if (StateDetector.TryDetectStart(out bool detected))
                        {
                            if (detected) State = SpectatorState.Running;
                        }
                    }
                    else if (State == SpectatorState.Paused)
                    {
                        if (StateDetector.TryIsRunning(out bool running))
                        {
                            if (running) State = SpectatorState.Running;
                        }
                    }
                    else if (State == SpectatorState.Running)
                    {
                        if (StateDetector.TryIsPaused(out bool paused))
                        {
                            if (paused) State = SpectatorState.Paused;
                        }
                    }
                    else if (stopwatch.Elapsed >= Replay.ReplayLength)
                    {
                        if (StateDetector.TryIsPaused(out bool paused))
                        {
                            State = SpectatorState.EndOfGame;
                        }
                    }

                    if (State == SpectatorState.Loading)
                    {
                        Thread.Sleep(100);
                    }
                    else
                    {
                        Thread.Sleep(500);
                    }

                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }

        private void LogTimerToConsole()
        {
            if ((int)Timer.TotalSeconds % 2 == 0)
            {
                Console.WriteLine("Timer: " + Timer);
            }
        }
    }
}