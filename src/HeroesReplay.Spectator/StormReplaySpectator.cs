using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Analyzer;
using HeroesReplay.Processes;
using HeroesReplay.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Spectator
{
    public sealed class StormReplaySpectator : IDisposable
    {
        public static TimeSpan KILL_STREAK_TIMER = TimeSpan.FromSeconds(12);
        public static TimeSpan MAX_PENTA_KILL_STREAK_POTENTIAL = KILL_STREAK_TIMER * 4;
        public static TimeSpan MAX_QUAD_KILL_STREAK_POTENTIAL = KILL_STREAK_TIMER * 3;
        public static TimeSpan MAX_TRIPLE_KILL_STREAK_POTENTIAL = KILL_STREAK_TIMER * 2;
        public static TimeSpan MAX_MULTI_KILL_STREAK_POTENTIAL = KILL_STREAK_TIMER * 1;

        public event EventHandler<GameEventArgs<StormPlayerDelta>>? HeroChange;
        public event EventHandler<GameEventArgs<GamePanel>>? PanelChange;
        public event EventHandler<GameEventArgs<GameStateDelta>>? StateChange;

        public GamePanel GamePanel
        {
            get => gamePanel;
            private set
            {
                if (gamePanel != value) PanelChange?.Invoke(this, new GameEventArgs<GamePanel>(StormReplay, value, GameTimer.Duration(), value.ToString()));
                gamePanel = value;
            }
        }

        public StormPlayer? CurrentPlayer
        {
            get => currentPlayer;
            private set
            {
                if (value != currentPlayer && value != null)
                {
                    HeroChange?.Invoke(this, new GameEventArgs<StormPlayerDelta>(StormReplay, new StormPlayerDelta(currentPlayer, value), GameTimer.Duration(), value.Criteria.ToString()));
                }

                currentPlayer = value;
            }
        }

        public GameState GameState
        {
            get => gameState;
            private set
            {
                if (gameState != value) StateChange?.Invoke(this, new GameEventArgs<GameStateDelta>(StormReplay, new GameStateDelta(gameState, value), GameTimer.Duration(), gameState.ToString()));
                gameState = value;
            }
        }

        public TimeSpan GameTimer { get; private set; }

        public StormReplay StormReplay
        {
            get => stormReplay;
            private set
            {
                GameState = GameState.StartOfGame;
                GameTimer = TimeSpan.Zero;
                GamePanel = GamePanel.CrowdControlEnemyHeroes;
                CurrentPlayer = null;
                stormReplay = value;
            }
        }

        private CancellationToken Token => tokenProvider.Token;

        private AnalyerResultBuilder Analyze => new AnalyerResultBuilder().WithSpectator(this).WithAnalyzer(analyzer);

        private GameState gameState = GameState.StartOfGame;
        private GamePanel gamePanel = GamePanel.CrowdControlEnemyHeroes;
        private StormPlayer? currentPlayer;
        private StormReplay stormReplay;
        private DeviceContextHolder deviceContextHolder;

        private readonly ILogger<StormReplaySpectator> logger;
        private readonly StormReplayHeroSelector selector;
        private readonly HeroesOfTheStorm heroesOfTheStorm;
        private readonly CancellationTokenProvider tokenProvider;
        private readonly StormReplayAnalyzer analyzer;


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
            try
            {
                StormReplay = stormReplay;

                deviceContextHolder = heroesOfTheStorm.AquireDeviceContext();

                await Task.WhenAll(Task.Run(PanelLoopAsync, Token), Task.Run(FocusLoopAsync, Token), Task.Run(StateLoopAsync, Token));
            }
            finally
            {
                deviceContextHolder.Dispose();
            }
        }

        private async Task FocusLoopAsync()
        {
            while (GameState != GameState.EndOfGame)
            {
                Token.ThrowIfCancellationRequested();

                // Timer detection will cause this to change
                if (GameState != GameState.Running) continue;

                // Let the heroes load in before making the initial selection
                if (GameTimer < TimeSpan.FromSeconds(10)) continue;

                try
                {
                    List<StormPlayer> selection = GetPlayersForFocus();

                    if (selection.Any(p => p.Criteria == GameCriteria.Alive || p.Criteria == GameCriteria.PreviousAliveKiller))
                    {
                        StormPlayer player = selection.Shuffle().Take(1).First();
                        if (CurrentPlayer?.Criteria == GameCriteria.Alive && CurrentPlayer.Player.Team == player.Player.Team) continue;
                        await FocusPlayerAsync(player, TimeSpan.FromSeconds(5));
                    }
                    else
                    {
                        foreach (StormPlayer player in selection)
                        {
                            await FocusPlayerAsync(player, (player.When - GameTimer).Add(TimeSpan.FromSeconds(2)));
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
        }

        private List<StormPlayer> GetPlayersForFocus()
        {
            IEnumerable<StormPlayer> pentaKillers = selector.Select(Analyze.Check(MAX_PENTA_KILL_STREAK_POTENTIAL), GameCriteria.PentaKill);
            IEnumerable<StormPlayer> quadKillers = selector.Select(Analyze.Check(MAX_QUAD_KILL_STREAK_POTENTIAL), GameCriteria.QuadKill);
            IEnumerable<StormPlayer> tripleKillers = selector.Select(Analyze.Check(MAX_TRIPLE_KILL_STREAK_POTENTIAL), GameCriteria.TripleKill);
            IEnumerable<StormPlayer> multiKillers = selector.Select(Analyze.Check(MAX_MULTI_KILL_STREAK_POTENTIAL), GameCriteria.MultiKill);
            IEnumerable<StormPlayer> singleKillers = selector.Select(Analyze.Check(KILL_STREAK_TIMER), GameCriteria.Kill);
            IEnumerable<StormPlayer> playerDeaths = selector.Select(Analyze.Check(KILL_STREAK_TIMER), GameCriteria.Death);

            IEnumerable<StormPlayer> mapObjectives = selector.Select(Analyze.Check(TimeSpan.FromSeconds(10)), GameCriteria.MapObjective);
            IEnumerable<StormPlayer> campCaptures = selector.Select(Analyze.Check(TimeSpan.FromSeconds(10)), GameCriteria.CampCapture);
            IEnumerable<StormPlayer> mercenaryKills = selector.Select(Analyze.Check(TimeSpan.FromSeconds(10)), GameCriteria.MercenaryKill);
            IEnumerable<StormPlayer> structures = selector.Select(Analyze.Check(TimeSpan.FromSeconds(10)), GameCriteria.Structure);
            IEnumerable<StormPlayer> previousKillers = selector.Select(Analyze.Check(TimeSpan.FromSeconds(10)), GameCriteria.PreviousAliveKiller);
            IEnumerable<StormPlayer> alivePlayers = selector.Select(Analyze.Check(TimeSpan.FromSeconds(6)), GameCriteria.Alive);

            return pentaKillers.Or(quadKillers.Or(tripleKillers.Or(multiKillers.Or(singleKillers.Or(playerDeaths.Or(mapObjectives.Or(campCaptures.Or(mercenaryKills.Or(structures.Or(previousKillers.Or(alivePlayers))))))))))).ToList();
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
                CurrentPlayer = stormPlayer;

                await Task.Delay(duration, Token);
            }
        }

        private async Task PanelLoopAsync()
        {
            while (GameState != GameState.EndOfGame)
            {
                Token.ThrowIfCancellationRequested();

                if (GameState != GameState.Running) continue;

                try
                {
                    if (GameTimer < TimeSpan.FromSeconds(60))
                    {
                        GamePanel = GamePanel.Talents;
                        continue;
                    }

                    AnalyzerResult result = Analyze.Check(TimeSpan.FromSeconds(10));

                    if (result.Talents.Any()) GamePanel = GamePanel.Talents;
                    else if (result.TeamObjectives.Any()) GamePanel = GamePanel.CarriedObjectives;
                    else if (result.MapObjectives.Any()) GamePanel = GamePanel.CarriedObjectives;
                    else if (result.Deaths.Any()) GamePanel = GamePanel.KillsDeathsAssists;
                    else if (result.Alive.Any())
                    {
                        GamePanel = GamePanel switch
                        {
                            GamePanel.KillsDeathsAssists => GamePanel.ActionsPerMinute,
                            GamePanel.ActionsPerMinute => GamePanel.CarriedObjectives,
                            GamePanel.CarriedObjectives => GamePanel.CrowdControlEnemyHeroes,
                            GamePanel.CrowdControlEnemyHeroes => GamePanel.DeathDamageRole,
                            GamePanel.DeathDamageRole => GamePanel.Experience,
                            GamePanel.Experience => GamePanel.Talents,
                            GamePanel.Talents => GamePanel.TimeDeadDeathsSelfSustain,
                            GamePanel.TimeDeadDeathsSelfSustain => GamePanel.KillsDeathsAssists
                        };
                    }

                    await result.WaitCheckTime(Token);
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
        }

        private async Task StateLoopAsync()
        {
            while (GameState != GameState.EndOfGame)
            {
                Token.ThrowIfCancellationRequested();

                try
                {
                    TimeSpan? elapsed = await heroesOfTheStorm.TryGetTimerAsync(deviceContextHolder);

                    // The timer is visible as 00:00 before the replay loads properly.
                    if (elapsed != null && elapsed < TimeSpan.Zero || elapsed > TimeSpan.Zero)
                    {
                        TimeSpan next = elapsed.Value.RemoveNegativeOffset();

                        if (next <= TimeSpan.Zero) GameState = GameState.StartOfGame;
                        else if (next > GameTimer) GameState = GameState.Running;
                        else if (next <= GameTimer) GameState = GameState.Paused;

                        GameTimer = next;
                    }
                    else
                    {
                        if (GameTimer.Add(TimeSpan.FromMinutes(1)) >= StormReplay.Replay.ReplayLength && await heroesOfTheStorm.TryGetMatchAwardsAsync(StormReplay.Replay.GetMatchAwards()))
                        {
                            GameState = GameState.EndOfGame;
                        }
                        else if (!heroesOfTheStorm.IsRunning)
                        {
                            GameState = GameState.EndOfGame;
                        }
                    }

                    if (CurrentPlayer != null)
                    {
                        logger.LogInformation($"[{CurrentPlayer.Player.HeroId}][{CurrentPlayer.Criteria}][{CurrentPlayer.When}][{GameTimer}][{GameTimer.AddNegativeOffset()}]");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), Token);
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(10), Token); // End of game
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

        public void Dispose()
        {
            HeroChange = null;
            PanelChange = null;
            StateChange = null;
        }
    }
}