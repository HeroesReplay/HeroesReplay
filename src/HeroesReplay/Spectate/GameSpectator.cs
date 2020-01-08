using Heroes.ReplayParser;
using Microsoft.Extensions.Logging;
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
    public sealed class GameSpectator : IDisposable
    {
        public static readonly GameMode[] SupportedModes = { GameMode.Brawl, GameMode.HeroLeague, GameMode.QuickMatch, GameMode.UnrankedDraft };

        public GamePanel CurrentPanel
        {
            get => currentPlanel;
            set
            {
                if (currentPlanel != value)
                {
                    PanelChange?.Invoke(this, new GameEventArgs<GamePanel>(Game, value, "Change"));
                }

                currentPlanel = value;
            }
        }

        (TimeSpan Timer, Unit Unit, Player Player, String Reason) CurrentFocus
        {
            get => currentFocus;
            set
            {
                if (currentFocus.Player != value.Player && value.Player != null)
                {
                    HeroChange?.Invoke(this, new GameEventArgs<Player>(Game, value.Player, value.Reason));
                }

                currentFocus = value;
            }
        }

        public GameState CurrentState
        {
            get => currentState;
            set
            {
                if (currentState != value)
                {
                    StateChange?.Invoke(this, new GameEventArgs<StateDelta>(Game, new StateDelta(currentState, value), $"{stopwatch.Elapsed}"));
                }

                if (value == GameState.Loading) stopwatch.Reset();
                else if (value == GameState.Running) stopwatch.Start();
                else if (value == GameState.Paused) stopwatch.Stop();

                currentState = value;
            }
        }

        public event EventHandler<GameEventArgs<Player>> HeroChange;
        public event EventHandler<GameEventArgs<GamePanel>> PanelChange;
        public event EventHandler<GameEventArgs<StateDelta>> StateChange;

        private readonly Stopwatch stopwatch = new Stopwatch();

        private GameState currentState;
        private GamePanel currentPlanel;
        private (TimeSpan Time, Unit Unit, Player Player, String Reason) currentFocus;

        private ViewBuilder viewBuilder;
        private readonly ILogger<GameSpectator> logger;
        private readonly StateDetector stateDetector;        
        private readonly List<GamePanel> panels = Enum.GetValues(typeof(GamePanel)).Cast<GamePanel>().ToList();

        public Game Game { get; private set; }

        public GameSpectator(ILogger<GameSpectator> logger, StateDetector stateDetector)
        {
            this.logger = logger;
            this.stateDetector = stateDetector;
        }

        public async Task RunAsync(Game game, CancellationToken token)
        {
            Game = game;
            CurrentState = GameState.Loading;
            viewBuilder = new ViewBuilder(stopwatch, Game);

            var panels = Task.Run(() => PanelLoopAsync(token), token);
            var players = Task.Run(() => FocusLoopAsync(token), token);
            var state = Task.Run(() => StartEndDetectorLoopAsync(token), token);

            await Task.WhenAll(state, players, panels).ConfigureAwait(false);
        }

        public void Dispose()
        {
            stateDetector?.Dispose();
        }

        private async Task FocusLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && CurrentState != GameState.EndOfGame)
            {
                while (CurrentState == GameState.Running && !token.IsCancellationRequested)
                {
                    try
                    {
                        using (var viewSpan = viewBuilder.TheNext(10).Seconds())
                        {
                            await HandleFocusAsync(viewSpan);
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, e.Message);
                    }
                }

                await Task.Delay(1000, token);
            }
        }

        private async Task HandleFocusAsync(ViewSpan viewSpan)
        {
            if (viewSpan.Deaths.Any())
            {
                foreach (Unit unit in viewSpan.Deaths)
                {
                    CurrentFocus = (viewSpan.Start, unit, unit.PlayerControlledBy, "Death");
                    await Task.Delay(unit.TimeSpanDied.Value - viewSpan.Start);
                }
            }
            else if (viewSpan.MapObjectives.Any())
            {
                var distribution =  TimeSpan.FromSeconds(viewSpan.Range.TotalSeconds / viewSpan.MapObjectives.Length);

                foreach (var unit in viewSpan.MapObjectives)
                {
                    CurrentFocus = (viewSpan.Start, null, unit.PlayerKilledBy ?? unit.PlayerControlledBy, "MapObjectives");
                    await Task.Delay(distribution);
                }
            }
            else if (viewSpan.TeamObjectives.Any())
            {
                var distribution = TimeSpan.FromSeconds(viewSpan.Range.TotalSeconds / viewSpan.TeamObjectives.Length);

                foreach (TeamObjective unit in viewSpan.TeamObjectives)
                {
                    CurrentFocus = (viewSpan.Start, null, unit.Player, "TeamObjectives");
                    await Task.Delay(distribution);
                }
            }
            else if (viewSpan.Structures.Any())
            {
                var distribution = TimeSpan.FromSeconds(viewSpan.Range.TotalSeconds / viewSpan.Structures.Length);

                foreach (Unit unit in viewSpan.Structures)
                {
                    CurrentFocus = (viewSpan.Start, unit, unit.PlayerKilledBy, "Structures");
                    await Task.Delay(distribution);
                }
            }
            else if (viewSpan.Alive.Any())
            {
                var distribution = TimeSpan.FromSeconds(viewSpan.Range.TotalSeconds / viewSpan.Alive.Length);

                foreach (Player player in viewSpan.Alive)
                {
                    CurrentFocus = (viewSpan.Start, null, player, "Alive");
                    await Task.Delay(distribution);
                }
            }
        }

        private async Task PanelLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && CurrentState != GameState.EndOfGame)
            {
                while (CurrentState == GameState.Running && !token.IsCancellationRequested)
                {
                    try
                    {
                        using (var viewSpan = viewBuilder.TheNext(10).Seconds())
                        {
                            if (viewSpan.Talents.Any()) CurrentPanel = GamePanel.Talents;
                            else if (viewSpan.TeamObjectives.Any() || viewSpan.MapObjectives.Any()) CurrentPanel = GamePanel.CarriedObjectives;
                            else if (viewSpan.Deaths.Any()) CurrentPanel = GamePanel.KillsDeathsAssists;
                            else
                            {
                                int next = panels.IndexOf(CurrentPanel) + 1;
                                CurrentPanel = next >= panels.Count ? panels.First() : panels[next];
                            }

                            await Task.Delay(viewSpan.Range);
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, e.Message);
                    }
                }

                await Task.Delay(1000, token);
            }
        }

        private async Task StartEndDetectorLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && CurrentState != GameState.EndOfGame)
            {
                try
                {
                    while(CurrentState == GameState.Loading)
                    {
                        logger.LogInformation("Waiting for start...");

                        if (CurrentState == GameState.Loading && stateDetector.TryDetectStart(out bool isLoaded) && isLoaded)
                        {
                            CurrentState = GameState.Running;
                        }

                        await Task.Delay(TimeSpan.FromSeconds(1), token);
                    }

                    var duration = stopwatch.Elapsed.Duration();

                    if (duration >= Game.Replay.ReplayLength && stateDetector.TryIsPaused(out bool isEnded) && isEnded)
                    {
                        CurrentState = GameState.EndOfGame;
                    }

                    Console.WriteLine("Timer: " + new TimeSpan(duration.Hours, duration.Minutes, duration.Seconds));
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
        }
    }
}