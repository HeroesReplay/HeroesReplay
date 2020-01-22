using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Analyzer;
using HeroesReplay.Processes;
using HeroesReplay.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Spectator
{
    public sealed class StormReplaySpectator : IDisposable
    {
        public event EventHandler<GameEventArgs<Player>>? HeroChange;
        public event EventHandler<GameEventArgs<GamePanel>>? PanelChange;
        public event EventHandler<GameEventArgs<GameStateDelta>>? StateChange;

        public GamePanel GamePanel
        {
            get => gamePanel;
            set
            {
                if (gamePanel != value) PanelChange?.Invoke(this, new GameEventArgs<GamePanel>(StormReplay, value, GameTimer.Duration(), value.ToString()));
                gamePanel = value;
            }
        }

        public StormPlayer StormPlayer
        {
            get => stormPlayer;
            set
            {
                if (value != null && value.Player != stormPlayer?.Player)
                {
                    HeroChange?.Invoke(this, new GameEventArgs<Player>(StormReplay, value.Player, GameTimer.Duration(), value.Criteria.ToString()));
                }

                stormPlayer = value;
            }
        }

        public GameState GameState
        {
            get => gameState;
            set
            {
                if (gameState != value)
                {
                    StateChange?.Invoke(this, new GameEventArgs<GameStateDelta>(StormReplay, new GameStateDelta(gameState, value), GameTimer.Duration(), gameState.ToString()));

                    gameState = value;
                }
            }
        }

        private CancellationToken Token => tokenProvider.Token;

        private AnalyerResultBuilder Analyze => new AnalyerResultBuilder().WithSpectator(this).WithAnalyzer(analyzer);

        private GameState gameState;
        private GamePanel gamePanel;
        private TimeSpan gameTimer;
        private StormPlayer stormPlayer;

        public TimeSpan GameTimer
        {
            get => gameTimer;
            set
            {
                if (value == TimeSpan.Zero)
                {
                    gameTimer = value;
                    GameState = GameState.StartOfGame;
                }
                else if (value >= StormReplay.Replay.ReplayLength)
                {
                    gameTimer = value;
                    GameState = GameState.EndOfGame;
                }
                else if (value > gameTimer)
                {
                    gameTimer = value;
                    GameState = GameState.Running;
                }
                else if (value <= gameTimer)
                {
                    gameTimer = value;
                    GameState = GameState.Paused;
                }
            }
        }

        public StormReplay StormReplay { get; set; }

        private readonly ILogger<StormReplaySpectator> logger;
        private readonly StormReplayHeroSelector selector;
        private readonly HeroesOfTheStorm heroesOfTheStorm;
        private readonly CancellationTokenProvider tokenProvider;
        private readonly StormReplayAnalyzer analyzer;
        private readonly List<GamePanel> panels = Enum.GetValues(typeof(GamePanel)).Cast<GamePanel>().ToList();

        public StormReplaySpectator(ILogger<StormReplaySpectator> logger, StormReplayHeroSelector selector, HeroesOfTheStorm heroesOfTheStorm, CancellationTokenProvider tokenProvider, StormReplayAnalyzer analyzer)
        {
            this.logger = logger;
            this.selector = selector;
            this.heroesOfTheStorm = heroesOfTheStorm;
            this.tokenProvider = tokenProvider;
            this.analyzer = analyzer;
        }

        public async Task SpectateAsync(StormReplay stormReplay)
        {
            StormReplay = stormReplay;
            GameState = GameState.StartOfGame;
            await Task.WhenAll(Task.Run(PanelLoopAsync, Token), Task.Run(FocusLoopAsync, Token), Task.Run(StateLoopAsync, Token));
        }

        public void Dispose()
        {
            HeroChange = null;
            PanelChange = null;
            StateChange = null;
        }

        private async Task FocusLoopAsync()
        {
            while (GameState != GameState.EndOfGame && !Token.IsCancellationRequested)
            {
                while ((GameState == GameState.Running || Debugger.IsAttached) && !Token.IsCancellationRequested)
                {
                    try
                    {
                        IEnumerable<StormPlayer> selection =
                            selector.Select(Analyze.Check(Constants.Heroes.MAX_PENTA_KILL_STREAK_POTENTIAL), GameCriteria.PentaKill).Or(
                                selector.Select(Analyze.Check(Constants.Heroes.MAX_QUAD_KILL_STREAK_POTENTIAL), GameCriteria.QuadKill).Or(
                                    selector.Select(Analyze.Check(Constants.Heroes.MAX_TRIPLE_KILL_STREAK_POTENTIAL), GameCriteria.TripleKill).Or(
                                        selector.Select(Analyze.Check(Constants.Heroes.MAX_MULTI_KILL_STREAK_POTENTIAL), GameCriteria.MultiKill).Or(
                                            selector.Select(Analyze.Check(Constants.Heroes.MAX_MULTI_KILL_STREAK_POTENTIAL), GameCriteria.Kill).Or(
                                                selector.Select(Analyze.Check(TimeSpan.FromSeconds(5)), GameCriteria.MapObjective).Or(
                                                    selector.Select(Analyze.Check(TimeSpan.FromSeconds(5)), GameCriteria.CampObjective).Or(
                                                        selector.Select(Analyze.Check(TimeSpan.FromSeconds(5)), GameCriteria.Structure).Or(
                                                            selector.Select(Analyze.Check(TimeSpan.FromSeconds(5)), GameCriteria.Alive)))))))));


                        if (selection.Any(p => p.Criteria == GameCriteria.Alive))
                        {
                            StormPlayer player = selection.Shuffle().Take(1).First();
                            if (StormPlayer?.Criteria == GameCriteria.Alive && StormPlayer.Player.Team == player.Player.Team) continue;
                            await FocusPlayerAsync(player, TimeSpan.FromSeconds(5));
                        }
                        else
                        {
                            foreach (StormPlayer player in selection)
                            {
                                await FocusPlayerAsync(player, player.When - GameTimer);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, e.Message);
                    }
                }
            }
        }

        private async Task FocusPlayerAsync(StormPlayer stormPlayer, TimeSpan duration)
        {
            // If the duration is negative, or the current timer + duration is still greater than when the timer it was calculated with or if the game timer is less than the timer it was taken again, we have an error
            if (duration <= TimeSpan.Zero || stormPlayer.Timer > GameTimer)
            {
                logger.LogInformation($"INVALID FOCUS: {stormPlayer.Player.Character}. REASON: {stormPlayer.Criteria}. DURATION: {duration}.");
            }
            else
            {
                StormPlayer = stormPlayer;

                await Task.Delay(duration, Token);
            }
        }

        private async Task PanelLoopAsync()
        {
            while (GameState != GameState.EndOfGame && !Token.IsCancellationRequested)
            {
                while (GameState == GameState.Running && !Token.IsCancellationRequested)
                {
                    try
                    {
                        AnalyzerResult result = Analyze.Check(TimeSpan.FromSeconds(5));

                        if (result.Talents.Any()) GamePanel = GamePanel.Talents;
                        else if (result.TeamObjectives.Any()) GamePanel = GamePanel.CarriedObjectives;
                        else if (result.MapObjectives.Any()) GamePanel = GamePanel.CarriedObjectives;
                        else if (result.PlayerDeaths.Any()) GamePanel = GamePanel.KillsDeathsAssists;
                        else if (result.PlayersAlive.Any())
                        {
                            GamePanel = panels.IndexOf(GamePanel) + 1 >= panels.Count ? panels.First() : panels[panels.IndexOf(GamePanel) + 1];
                        }
                        else
                        {
                            GamePanel = GamePanel.KillsDeathsAssists;
                        }

                        await result.WaitCheckTime(Token);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, e.Message);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(5), Token);
            }
        }

        private void PrintDebugData()
        {
            foreach (TrackerEvent trackerEvent in StormReplay.Replay.TrackerEvents.Where(e => e.TimeSpan == GameTimer))
            {
                string data = trackerEvent.TrackerEventType switch
                {
                    ReplayTrackerEvents.TrackerEventType.UnitRevivedEvent => $"[{trackerEvent.TrackerEventType}][{trackerEvent.Data}]",
                    ReplayTrackerEvents.TrackerEventType.UnitOwnerChangeEvent => $"[{trackerEvent.TrackerEventType}][{trackerEvent.Data}]",
                    _ => string.Empty,
                };

                if (!string.IsNullOrEmpty(data))
                {
                    logger.LogInformation($"[TrackerEvent]{data}");
                }
            }

            foreach (GameEvent gameEvent in StormReplay.Replay.GameEvents.Where(e => e.TimeSpan == GameTimer))
            {
                string data = gameEvent.eventType switch
                {
                    GameEventType.CStartGameEvent => $"[{gameEvent.eventType}][{gameEvent.player.Character}][{gameEvent.data}]",
                    GameEventType.CTriggerPingEvent => $"[{gameEvent.eventType}][{gameEvent.player.Character}][{gameEvent.data}]",
                    GameEventType.CUnitClickEvent => $"[{gameEvent.eventType}][{gameEvent.player.Character}][{gameEvent.data}]",
                    GameEventType.CCmdEvent => $"[{gameEvent.eventType}][{gameEvent.player.Character}][{gameEvent.data}]",
                    _ => string.Empty,
                };

                if (!string.IsNullOrEmpty(data))
                {
                    logger.LogInformation($"[GameEvent]{data}");
                }
            }

            foreach (Unit unit in StormReplay.Replay.Units.Where(u => u.TimeSpanBorn <= GameTimer && u.TimeSpanDied.HasValue && u.TimeSpanDied.Value >= GameTimer))
            {
                string data = unit.Group switch
                {
                    Unit.UnitGroup.MercenaryCamp => $"[{unit.Group}][{unit.Name}]",
                    Unit.UnitGroup.MapObjective => $"[{unit.Group}][{unit.Name}]",
                    Unit.UnitGroup.Miscellaneous => $"[{unit.Group}][{unit.Name}]",
                    _ => string.Empty
                };

                if (!string.IsNullOrEmpty(data))
                {
                    logger.LogInformation($"[Unit]{data}");
                }
            }
        }

        private async Task StateLoopAsync()
        {
            while (GameState != GameState.EndOfGame && !Token.IsCancellationRequested)
            {
                try
                {
                    TimeSpan? elapsed = await heroesOfTheStorm.TryGetTimerAsync();

                    if (elapsed.HasValue)
                    {
                        GameTimer = elapsed.Value.RemoveNegativeOffset();
                    }

                    if (StormPlayer != null)
                    {
                        logger.LogInformation($"[{StormPlayer.Player.Character}][{StormPlayer.Criteria}][{StormPlayer.When}][{GameTimer}][{GameTimer.AddNegativeOffset()}]");
                    }

                    // PrintDebugData();

                    await Task.Delay(TimeSpan.FromSeconds(0.9), Token);
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
        }
    }
}