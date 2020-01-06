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
        public static readonly List<GameMode> SupportedModes = new List<GameMode> { GameMode.Brawl, GameMode.HeroLeague, GameMode.QuickMatch, GameMode.UnrankedDraft };

        public GamePanel Current
        {
            get => current;
            set
            {
                if (current != value)
                {
                    PanelChange?.Invoke(this, new EventData<GamePanel>(Replay, value, "Change"));
                }

                current = value;
            }
        }
        
        public GameState GameState
        {
            get => gameState;
            set
            {
                if (gameState != value)
                {
                    StateChange?.Invoke(this, new EventData<StateDelta>(Replay, new StateDelta(gameState, value), $"{stopwatch.Elapsed}"));
                }

                if (value == GameState.EndOfGame) stopwatch.Stop();
                if (value == GameState.Running) stopwatch.Start();
                if (value == GameState.Paused) stopwatch.Stop();
                
                gameState = value;
            }
        }

        public TimeSpan PanelWait { get; set; } = TimeSpan.FromSeconds(10);

        public event EventHandler<EventData<Player>> HeroChange;
        public event EventHandler<EventData<GamePanel>> PanelChange;
        public event EventHandler<EventData<StateDelta>> StateChange;

        public TimeSpan Timer => stopwatch.Elapsed;

        private List<Player> bluePlayers = new List<Player>();
        private List<Player> redPlayers = new List<Player>();

        private Stopwatch stopwatch = new Stopwatch();

        private Thread panelThread;
        private Thread playerThread;
        private Thread stateThread;
        
        private GameState gameState;
        private GamePanel current;

        private readonly Game game;
        private readonly ViewBuilder viewBuilder;
        private readonly StateDetector stateDetector;
        private readonly List<GamePanel> panels = Enum.GetValues(typeof(GamePanel)).Cast<GamePanel>().ToList();

        private Replay Replay => game.Replay;

        public GameSpectator(Game game)
        {
            this.game = game;
            this.stateDetector = new StateDetector();
            this.viewBuilder = new ViewBuilder(stopwatch, Replay);

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
            GameState = GameState.Paused;
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
                        var viewSpan = viewBuilder.TheNext(10).Seconds;

                        if (viewSpan.Deaths.Any())
                        {
                            // TODO: better logic

                            foreach (var unit in viewSpan.Deaths)
                            {
                                var reason =  unit.PlayerKilledBy == unit.PlayerControlledBy ? "Suicide" : (unit.PlayerKilledBy != null ? "Death" : "Environment");
                                HeroChange?.Invoke(this, new EventData<Player>(Replay, unit.PlayerKilledBy ?? unit.PlayerControlledBy, reason));
                                Thread.Sleep(TimeSpan.FromSeconds(3)); 
                            }
                        }
                        else if (viewSpan.MapObjectives.Any())
                        {
                            // TODO: better logic
                            foreach (var unit in viewSpan.MapObjectives)
                            {
                                HeroChange?.Invoke(this, new EventData<Player>(Replay, unit.PlayerKilledBy ?? unit.PlayerControlledBy, "MapObjectives"));
                                Thread.Sleep(2000);
                            }
                        }
                        else if (viewSpan.TeamObjectives.Any())
                        {
                            // TODO: better logic

                            foreach (var unit in viewSpan.TeamObjectives)
                            {
                                if (unit.Player != null)
                                {
                                    HeroChange?.Invoke(this, new EventData<Player>(Replay, unit.Player, "TeamObjectives"));
                                    Thread.Sleep(2000);
                                }
                            }
                        }
                        else if (viewSpan.Structures.Any())
                        {
                            // TODO: better logic

                            foreach (var unit in viewSpan.Structures)
                            {
                                HeroChange?.Invoke(this, new EventData<Player>(Replay, unit.PlayerKilledBy ?? unit.PlayerControlledBy, "Structures"));
                                Thread.Sleep(5000);
                            }
                        }
                        else if (viewSpan.Alive.Any())
                        {
                            // TODO: better logic

                            // Give every alive player equal view time?
                            foreach (var player in viewSpan.Alive)
                            {
                                HeroChange?.Invoke(this, new EventData<Player>(Replay, player, "Alive"));
                                Thread.Sleep(TimeSpan.FromSeconds(10.0 / viewSpan.Alive.Count()));
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
                        var viewSpan = viewBuilder.TheNext(5).Seconds;

                        if (viewSpan.Talents.Any())
                        {
                            Current = GamePanel.Talents;
                        }
                        else if (viewSpan.TeamObjectives.Any())
                        {
                            Current = GamePanel.CarriedObjectives;
                        }
                        else if (viewSpan.Deaths.Any())
                        {
                            Current = GamePanel.KillsDeathsAssists;
                        }
                        else
                        {
                            int next = panels.IndexOf(Current) + 1;
                            Current = next >= panels.Count ? panels.First() : panels[next];
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
                    if (GameState == GameState.Loading)
                    {
                        if (stateDetector.TryDetectStart(out bool detected))
                        {
                            if (detected) GameState = GameState.Running;
                        }
                    }
                    else if (GameState == GameState.Paused)
                    {
                        if (stateDetector.TryIsRunning(out bool running))
                        {
                            if (running) GameState = GameState.Running;
                        }
                    }
                    else if (GameState == GameState.Running)
                    {
                        if (stateDetector.TryIsPaused(out bool paused))
                        {
                            if (paused) GameState = GameState.Paused;
                        }
                    }
                    else if (stopwatch.Elapsed >= Replay.ReplayLength)
                    {
                        if (stateDetector.TryIsPaused(out bool ended))
                        {
                            if (ended) GameState = GameState.EndOfGame;
                        }
                    }

                    if (GameState == GameState.Loading)
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