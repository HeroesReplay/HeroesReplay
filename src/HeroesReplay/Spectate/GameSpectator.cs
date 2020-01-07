using Heroes.ReplayParser;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay
{
    /// <summary>
    /// The Game spectator manages the state (paused, running, start, end) and fires events when it detects these changes.
    /// The Game spectator does NOT send commands to the game, it only raises events to which the game controller should respond with.
    /// </summary>
    public class GameSpectator : IDisposable
    {
        public static readonly GameMode[] SupportedModes = { GameMode.Brawl, GameMode.HeroLeague, GameMode.QuickMatch, GameMode.UnrankedDraft };

        public GamePanel Current
        {
            get => current;
            set
            {
                if (current != value)
                {
                    PanelChange?.Invoke(this, new GameEvent<GamePanel>(Game, value, "Change"));
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
                    StateChange?.Invoke(this, new GameEvent<StateDelta>(Game, new StateDelta(gameState, value), $"{stopwatch.Elapsed}"));
                }

                if (value == GameState.Loading) stopwatch.Reset();
                else if (value == GameState.Running) stopwatch.Start();
                else if (value == GameState.Paused) stopwatch.Stop();
                
                gameState = value;
            }
        }

        public event EventHandler<GameEvent<Player>> HeroChange;
        public event EventHandler<GameEvent<GamePanel>> PanelChange;
        public event EventHandler<GameEvent<StateDelta>> StateChange;

        private Stopwatch stopwatch = new Stopwatch();
        private GameState gameState;
        private GamePanel current;
        
        private ViewBuilder ViewBuilder => new ViewBuilder(stopwatch, Game);
        private readonly StateDetector stateDetector;
        private readonly List<GamePanel> panels = Enum.GetValues(typeof(GamePanel)).Cast<GamePanel>().ToList();

        public Game Game { get; private set; }
        
        public GameSpectator(StateDetector stateDetector)
        {
            this.stateDetector = stateDetector;            
        }

        public async Task RunAsync(Game game, CancellationToken token)
        {
            Game = game;
            GameState = GameState.Loading;

            var panels = Task.Run(() => PanelLoopAsync(token), token);
            var players = Task.Run(() => PlayerLoopAsync(token), token);
            var state = Task.Run(() => StartEndDetectorLoop(token), token);

            await Task.WhenAll(state, players, panels);
        }

        public void Dispose() => stateDetector?.Dispose();

        private async Task PlayerLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && GameState != GameState.EndOfGame)
            {
                while (stopwatch.IsRunning && !token.IsCancellationRequested)
                {
                    try
                    {
                        var viewSpan = ViewBuilder.TheNext(10).Seconds;

                        if (viewSpan.Deaths.Any())
                        {
                            // TODO: better logic
                            foreach (var unit in viewSpan.Deaths)
                            {
                                var reason =  unit.PlayerKilledBy == unit.PlayerControlledBy ? "Suicide" : (unit.PlayerKilledBy != null ? "Death" : "Environment");
                                HeroChange?.Invoke(this, new GameEvent<Player>(Game, unit.PlayerControlledBy, reason));
                                await Task.Delay(unit.TimeSpanDied.Value - stopwatch.Elapsed); 
                            }
                        }
                        else if (viewSpan.MapObjectives.Any())
                        {
                            // TODO: better logic
                            foreach (var unit in viewSpan.MapObjectives)
                            {
                                HeroChange?.Invoke(this, new GameEvent<Player>(Game, unit.PlayerKilledBy ?? unit.PlayerControlledBy, "MapObjectives"));
                                await Task.Delay(unit.TimeSpanDied.Value - stopwatch.Elapsed);
                            }
                        }
                        else if (viewSpan.TeamObjectives.Any())
                        {
                            // TODO: better logic

                            foreach (var unit in viewSpan.TeamObjectives)
                            {
                                HeroChange?.Invoke(this, new GameEvent<Player>(Game, unit.Player, "TeamObjectives"));
                                await Task.Delay(unit.TimeSpan - stopwatch.Elapsed);
                            }
                        }
                        else if (viewSpan.Structures.Any())
                        {
                            foreach (var unit in viewSpan.Structures)
                            {
                                HeroChange?.Invoke(this, new GameEvent<Player>(Game, unit.PlayerKilledBy ?? unit.PlayerControlledBy, "Structures"));
                                await Task.Delay(unit.TimeSpanDied.Value - stopwatch.Elapsed);
                            }
                        }
                        else if (viewSpan.Alive.Any())
                        {
                            foreach (var player in viewSpan.Alive.OrderBy(x => Guid.NewGuid()).Take(2))
                            {
                                HeroChange?.Invoke(this, new GameEvent<Player>(Game, player, "Alive"));
                                await Task.Delay(TimeSpan.FromSeconds(5));
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }

                await Task.Delay(100);
            }
        }

        private async Task PanelLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && GameState != GameState.EndOfGame)
            {
                while (stopwatch.IsRunning && !token.IsCancellationRequested)
                {
                    try
                    {   
                        var viewSpan = ViewBuilder.TheNext(5).Seconds;

                        if (viewSpan.Talents.Any()) Current = GamePanel.Talents;
                        else if (viewSpan.TeamObjectives.Any() || viewSpan.MapObjectives.Any()) Current = GamePanel.CarriedObjectives;
                        else if (viewSpan.Deaths.Any()) Current = GamePanel.KillsDeathsAssists;
                        else
                        {
                            int next = panels.IndexOf(Current) + 1;
                            Current = next >= panels.Count ? panels.First() : panels[next];
                        }

                        await Task.Delay(viewSpan.Upper);

                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }

                await Task.Delay(100);
            }
        }

        private async Task StartEndDetectorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && GameState != GameState.EndOfGame)
            {
                if (stopwatch.Elapsed.Seconds % 2 == 0) Console.WriteLine("Timer: " + stopwatch.Elapsed);

                try
                {
                    if (GameState == GameState.Loading && stateDetector.TryDetectStart(out bool isLoaded) && isLoaded) GameState = GameState.Running;
                    else if (stopwatch.Elapsed >= Game.Replay.ReplayLength && stateDetector.TryIsPaused(out bool isEnded) && isEnded) GameState = GameState.EndOfGame;

                    if (GameState == GameState.Running) await Task.Delay(TimeSpan.FromSeconds(1)); // we've begun, we dont need to check so often
                    else if(GameState == GameState.Loading) await Task.Delay(TimeSpan.FromSeconds(0.25)); // we have not detected the start of the game, check rigorously 
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }
    }
}