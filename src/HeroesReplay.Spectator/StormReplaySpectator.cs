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
        public event EventHandler<GameEventArgs<StormPlayerDelta>>? HeroChange;
        public event EventHandler<GameEventArgs<GamePanel>>? PanelChange;
        public event EventHandler<GameEventArgs<GameStateDelta>>? StateChange;

        public GamePanel GamePanel
        {
            get => gamePanel;
            private set
            {
                if (gamePanel != value)
                {
                    logger.LogDebug($"[PanelChange][{gamePanel}]");
                    PanelChange?.Invoke(this, new GameEventArgs<GamePanel>(StormReplay, value, GameTimer.Duration(), value.ToString()));
                }

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
                    logger.LogDebug($"[HeroChange][{value.Player.HeroId}]");
                    HeroChange?.Invoke(this, new GameEventArgs<StormPlayerDelta>(StormReplay, new StormPlayerDelta(currentPlayer, value), GameTimer.Duration(), value.Event.ToString()));
                }

                currentPlayer = value;
            }
        }

        public GameState GameState
        {
            get => gameState;
            private set
            {
                if (gameState != value)
                {
                    logger.LogDebug($"[StateChange][{gameState}]");
                    StateChange?.Invoke(this, new GameEventArgs<GameStateDelta>(StormReplay, new GameStateDelta(gameState, value), GameTimer.Duration(), gameState.ToString()));
                }
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
            StormReplay = stormReplay ?? throw new ArgumentNullException(nameof(stormReplay));

            await Task.WhenAll(Task.Run(PanelLoopAsync, Token), Task.Run(FocusLoopAsync, Token), Task.Run(StateLoopAsync, Token));
        }

        private async Task FocusLoopAsync()
        {
            while (!GameState.IsEnd())
            {
                Token.ThrowIfCancellationRequested();

                if (!GameState.IsRunning() || GameState.IsEnd() || GameTimer.IsNearStart()) continue;

                try
                {
                    List<StormPlayer> selection = GetPlayersForFocus();

                    if (selection.Any(p => p.IsEventIn(GameEvent.Alive, GameEvent.KilledEnemy, GameEvent.DangerZone)))
                    {
                        foreach (StormPlayer player in selection.Shuffle())
                        {
                            if (CurrentPlayer?.Player?.Team == player.Player.Team) continue;
                            await FocusPlayerAsync(player, TimeSpan.FromSeconds(5));
                            break;
                        }
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
            IEnumerable<StormPlayer> pentaKillers = selector.Select(Analyze.Check(Constants.MAX_PENTA_KILL_STREAK_POTENTIAL), GameEvent.PentaKill);
            IEnumerable<StormPlayer> quadKillers = selector.Select(Analyze.Check(Constants.MAX_QUAD_KILL_STREAK_POTENTIAL), GameEvent.QuadKill);
            IEnumerable<StormPlayer> tripleKillers = selector.Select(Analyze.Check(Constants.MAX_TRIPLE_KILL_STREAK_POTENTIAL), GameEvent.TripleKill);
            IEnumerable<StormPlayer> multiKillers = selector.Select(Analyze.Check(Constants.MAX_MULTI_KILL_STREAK_POTENTIAL), GameEvent.MultiKill);
            IEnumerable<StormPlayer> singleKillers = selector.Select(Analyze.Check(Constants.KILL_STREAK_TIMER), GameEvent.Kill);
            IEnumerable<StormPlayer> playerDeaths = selector.Select(Analyze.Check(Constants.KILL_STREAK_TIMER), GameEvent.Death);
            IEnumerable<StormPlayer> previousKillers = selector.Select(Analyze.Check(TimeSpan.FromSeconds(10)), GameEvent.KilledEnemy);
            IEnumerable<StormPlayer> mapObjectives = selector.Select(Analyze.Check(TimeSpan.FromSeconds(10)), GameEvent.MapObjective);
            IEnumerable<StormPlayer> campCaptures = selector.Select(Analyze.Check(TimeSpan.FromSeconds(10)), GameEvent.MercenaryCamp);
            IEnumerable<StormPlayer> mercenaryKills = selector.Select(Analyze.Check(TimeSpan.FromSeconds(5)), GameEvent.MercenaryKill);
            IEnumerable<StormPlayer> dangerZone = selector.Select(Analyze.Check(TimeSpan.FromSeconds(5)), GameEvent.DangerZone);
            IEnumerable<StormPlayer> structures = selector.Select(Analyze.Check(TimeSpan.FromSeconds(5)), GameEvent.Structure);
            IEnumerable<StormPlayer> alivePlayers = selector.Select(Analyze.Check(TimeSpan.FromSeconds(5)), GameEvent.Alive);

            return
                pentaKillers.Or(
                    quadKillers.Or(
                        tripleKillers.Or(
                            multiKillers.Or(
                                singleKillers.Or(
                                    playerDeaths.Or(
                                        mapObjectives.Or(
                                            campCaptures.Or(
                                                mercenaryKills.Or(
                                                    structures.Or(
                                                        dangerZone.Or(
                                                            previousKillers.Or(
                                                                alivePlayers)))))))))))).ToList();
        }

        private async Task FocusPlayerAsync(StormPlayer stormPlayer, TimeSpan duration)
        {
            // If the duration is negative, or the current timer + duration is still greater than when the timer it was calculated with or if the game timer is less than the timer it was taken again, we have an error
            if (duration <= TimeSpan.Zero || stormPlayer.Timer > GameTimer)
            {
                logger.LogDebug($"INVALID FOCUS: {stormPlayer.Player.Character}. REASON: {stormPlayer.Event}. DURATION: {duration}.");
            }
            else
            {
                CurrentPlayer = stormPlayer;

                await Task.Delay(duration, Token);
            }
        }

        private async Task PanelLoopAsync()
        {
            while (!GameState.IsEnd())
            {
                Token.ThrowIfCancellationRequested();

                if (!GameState.IsRunning()) continue;

                try
                {
                    if (GameTimer < TimeSpan.FromMinutes(1))
                    {
                        GamePanel = GamePanel.Talents;
                        continue;
                    }

                    AnalyzerResult result = Analyze.Check(TimeSpan.FromSeconds(5));

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
                            GamePanel.TimeDeadDeathsSelfSustain => GamePanel.KillsDeathsAssists,
                            _ => GamePanel.DeathDamageRole
                        };
                    }

                    await Task.Delay(TimeSpan.FromSeconds(10), Token);
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
        }

        private async Task StateLoopAsync()
        {
            while (!GameState.IsEnd())
            {
                Token.ThrowIfCancellationRequested();

                try
                {
                    TimeSpan? elapsed = await heroesOfTheStorm.TryGetTimerAsync();

                    // The timer is visible as 00:00 before the replay loads properly.
                    if (elapsed != null && elapsed != TimeSpan.Zero)
                    {
                        TimeSpan next = elapsed.Value.RemoveNegativeOffset();

                        if (next <= TimeSpan.Zero) GameState = GameState.StartOfGame;
                        else if (next > GameTimer) GameState = GameState.Running;
                        else if (next <= GameTimer) GameState = GameState.Paused;

                        GameTimer = next;
                    }
                    else
                    {
                        if (GameTimer.IsNearEnd(stormReplay.Replay.ReplayLength) && await heroesOfTheStorm.TryGetMatchAwardsAsync(StormReplay.Replay.GetMatchAwards()))
                        {
                            GameState = GameState.EndOfGame;
                        }
                        else if (!heroesOfTheStorm.IsRunning)
                        {
                            GameState = GameState.EndOfGame;
                        }
                    }

                    if (CurrentPlayer != null && GameTimer > TimeSpan.Zero)
                    {
                        logger.LogInformation($"[{CurrentPlayer.Player.HeroId}][{CurrentPlayer.Event}][{CurrentPlayer.When}][{GameTimer}][{GameTimer.AddNegativeOffset()}]");
                    }

                    // PrintDebugData();

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
                    logger.LogDebug($"[TrackerEvent]{data}");
                }
            }

            foreach (Heroes.ReplayParser.MPQFiles.GameEvent gameEvent in StormReplay.Replay.GameEvents.Where(e => e.TimeSpan == GameTimer))
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
                    logger.LogDebug($"[GameEvent]{data}");
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
                    logger.LogDebug($"[Unit]{data}");
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