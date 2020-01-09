using Heroes.ReplayParser;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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
        public event EventHandler<GameEventArgs<Player>> HeroChange;
        public event EventHandler<GameEventArgs<GamePanel>> PanelChange;
        public event EventHandler<GameEventArgs<StateDelta>> StateChange;

        public static readonly GameMode[] SupportedModes = { GameMode.Brawl, GameMode.HeroLeague, GameMode.QuickMatch, GameMode.UnrankedDraft };

        public GamePanel Panel
        {
            get => panel;
            set
            {
                if (panel != value) PanelChange?.Invoke(this, new GameEventArgs<GamePanel>(Game, value, "Change"));
                panel = value;
            }
        }

        public (TimeSpan Timer, Unit Unit, Player Player, String Reason) Focus
        {
            get => focus;
            set
            {
                if (focus.Player != value.Player && value.Player != null) HeroChange?.Invoke(this, new GameEventArgs<Player>(Game, value.Player, value.Reason));

                focus = value;
            }
        }

        public GameState State
        {
            get => state;
            set
            {
                if (state != value) StateChange?.Invoke(this, new GameEventArgs<StateDelta>(Game, new StateDelta(state, value), $"{Timer}"));
                state = value;
            }
        }

        private ViewBuilder ViewBuilder { get; set; }

        private GameState state;
        private GamePanel panel;
        private TimeSpan timer;
        private (TimeSpan Time, Unit Unit, Player Player, String Reason) focus;
        
        public TimeSpan Timer 
        {
            get => timer;
            set 
            {
                if (value != timer) logger.LogInformation($"Timer: {timer}. Now: {DateTime.Now}. Thread: {Thread.CurrentThread.ManagedThreadId}");

                if (value == TimeSpan.Zero) State = GameState.StartOfGame;
                else if (value >= Game.Replay.ReplayLength) State = GameState.EndOfGame;
                else if (value > Timer) State = GameState.Running;
                else if (value <= Timer) State = GameState.Paused;

                timer = value;
            }
        }

        public Game Game { get; set; }

        private readonly ILogger<GameSpectator> logger;
        private readonly StateDetector stateDetector;
        private readonly List<GamePanel> panels = Enum.GetValues(typeof(GamePanel)).Cast<GamePanel>().ToList();

        public GameSpectator(ILogger<GameSpectator> logger, StateDetector stateDetector)
        {
            this.logger = logger;
            this.stateDetector = stateDetector;
        }

        public async Task RunAsync(Game game, CancellationToken token)
        {
            Game = game;
            State = GameState.StartOfGame;
            ViewBuilder = new ViewBuilder(this);

            var panels = Task.Run(() => PanelLoopAsync(token), token);
            var players = Task.Run(() => FocusLoopAsync(token), token);
            var state = Task.Run(() => StateLoopAsync(token), token);

            await Task.WhenAll(state, players, panels);
        }

        public void Dispose()
        {
            
        }

        private async Task FocusLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && State != GameState.EndOfGame)
            {
                while (State == GameState.Running && !token.IsCancellationRequested)
                {
                    try
                    {
                        using (ViewSpan viewSpan = ViewBuilder.TheNext(10).Seconds())
                        {
                            await HandleFocusAsync(viewSpan, token);
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, e.Message);
                    }
                }
            }
        }

        private async Task HandleFocusAsync(ViewSpan viewSpan, CancellationToken token)
        {
            if (viewSpan.Deaths.Any())
            {
                foreach (Unit unit in viewSpan.Deaths)
                {
                    var diff = unit.TimeSpanDied.Value - viewSpan.Start;
                    Focus = (viewSpan.Start, unit, unit.PlayerControlledBy, $"Death in {diff}");
                    await Task.Delay(diff);
                }
            }
            else if (viewSpan.MapObjectives.Any())
            {
                foreach (var unit in viewSpan.MapObjectives)
                {
                    var diff = unit.TimeSpanDied.Value - viewSpan.Start;
                    Focus = (viewSpan.Start, null, unit.PlayerKilledBy ?? unit.PlayerControlledBy, $"MapObjective in {diff}");
                    await Task.Delay(unit.TimeSpanDied.Value - viewSpan.Start);
                }
            }
            else if (viewSpan.TeamObjectives.Any())
            {
                foreach (TeamObjective objective in viewSpan.TeamObjectives)
                {
                    var diff = objective.TimeSpan - viewSpan.Start;
                    Focus = (viewSpan.Start, null, objective.Player, $"TeamObjective in {diff}");
                    await Task.Delay(diff);
                }
            }
            else if (viewSpan.Structures.Any())
            {
                foreach (Unit unit in viewSpan.Structures.Take(2))
                {
                    var diff = unit.TimeSpanDied.Value - viewSpan.Start;
                    Focus = (viewSpan.Start, unit, unit.PlayerKilledBy, $"Structure in {diff}");
                    await Task.Delay(diff, token);
                }
            }
            else if (viewSpan.Alive.Any())
            {
                foreach (Player player in viewSpan.Alive.Take(2))
                {
                    Focus = (viewSpan.Start, null, player, "Alive");
                    await Task.Delay(5000, token);
                }
            }
        }

        private async Task PanelLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && State != GameState.EndOfGame)
            {
                while (State == GameState.Running && !token.IsCancellationRequested)
                {
                    try
                    {
                        using (var viewSpan = ViewBuilder.TheNext(10).Seconds())
                        {
                            if (viewSpan.Talents.Any()) Panel = GamePanel.Talents;
                            else if (viewSpan.TeamObjectives.Any() || viewSpan.MapObjectives.Any()) Panel = GamePanel.CarriedObjectives;
                            else if (viewSpan.Deaths.Any()) Panel = GamePanel.KillsDeathsAssists;
                            else
                            {
                                int next = panels.IndexOf(Panel) + 1;
                                Panel = next >= panels.Count ? panels.First() : panels[next];
                            }

                            await Task.Delay(viewSpan.Range, token);
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

        private async Task StateLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && State != GameState.EndOfGame)
            {
                try
                {
                    var (success, timer) = await stateDetector.TryGetTimerAsync();

                    if (success)
                    {
                        Timer = timer;
                    }

                    await Task.Delay(1000, token);
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
        }
    }
}