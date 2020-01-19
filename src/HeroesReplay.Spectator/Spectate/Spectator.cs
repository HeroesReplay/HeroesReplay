using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Spectator
{
    public sealed class Spectator : IDisposable
    {
        public event EventHandler<GameEventArgs<Player>>? HeroChange;
        public event EventHandler<GameEventArgs<Panel>>? PanelChange;
        public event EventHandler<GameEventArgs<StateDelta>>? StateChange;

        public Panel Panel
        {
            get => panel;
            set
            {
                if (panel != value) PanelChange?.Invoke(this, new GameEventArgs<Panel>(StormReplay, value, Timer.Duration(), value.ToString()));
                panel = value;
            }
        }

        public StormPlayer StormPlayer
        {
            get => stormPlayer;
            set
            {
                if (value != null && value.Player != stormPlayer?.Player)
                {
                    HeroChange?.Invoke(this, new GameEventArgs<Player>(StormReplay, value.Player, Timer.Duration(), value.Criteria.ToString()));
                }

                stormPlayer = value;
            }
        }

        public State State
        {
            get => state;
            set
            {
                if (state != value)
                {
                    StateChange?.Invoke(this, new GameEventArgs<StateDelta>(StormReplay, new StateDelta(state, value), Timer.Duration(), state.ToString()));

                    state = value;
                }
            }
        }

        private CancellationToken Token => tokenProvider.Token;

        private AnalyerResultBuilder Analyze => new AnalyerResultBuilder().WithSpectator(this).WithAnalyzer(analyzer);

        private State state;
        private Panel panel;
        private TimeSpan timer;
        private StormPlayer stormPlayer;

        public TimeSpan Timer
        {
            get => timer;
            set
            {
                if (value == TimeSpan.Zero)
                {
                    timer = value;
                    State = State.StartOfGame;
                }
                else if (value >= StormReplay.Replay.ReplayLength)
                {
                    timer = value;
                    State = State.EndOfGame;
                }
                else if (value > timer)
                {
                    timer = value;
                    State = State.Running;
                }
                else if (value <= timer)
                {
                    timer = value;
                    State = State.Paused;
                }
            }
        }

        public StormReplay StormReplay { get; set; }



        private readonly ILogger<Spectator> logger;
        private readonly StormPlayerSelector selector;
        private readonly HeroesOfTheStorm heroesOfTheStorm;
        private readonly CancellationTokenProvider tokenProvider;
        private readonly StormReplayAnalyzer analyzer;
        private readonly List<Panel> panels = Enum.GetValues(typeof(Panel)).Cast<Panel>().ToList();

        public Spectator(ILogger<Spectator> logger, StormPlayerSelector selector, HeroesOfTheStorm heroesOfTheStorm, CancellationTokenProvider tokenProvider, StormReplayAnalyzer analyzer)
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
            State = State.StartOfGame;
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
            while (State != State.EndOfGame && !Token.IsCancellationRequested)
            {
                while ((State == State.Running || Debugger.IsAttached) && !Token.IsCancellationRequested)
                {
                    try
                    {
                        List<StormPlayer> penta = selector.Select(Analyze.Check(Constants.Heroes.MAX_PENTA_KILL_STREAK_POTENTIAL), SelectorCriteria.PentaKill);

                        if (penta.Any())
                        {
                            foreach (StormPlayer player in penta)
                                await FocusPlayer(player, player.When - Timer);
                        }
                        else
                        {
                            List<StormPlayer> quad = selector.Select(Analyze.Check(Constants.Heroes.MAX_QUAD_KILL_STREAK_POTENTIAL), SelectorCriteria.QuadKill);

                            if (quad.Any())
                            {
                                foreach (StormPlayer player in quad)
                                    await FocusPlayer(player, player.When - Timer);
                            }
                            else
                            {
                                List<StormPlayer> triple = selector.Select(Analyze.Check(Constants.Heroes.MAX_TRIPLE_KILL_STREAK_POTENTIAL), SelectorCriteria.TripleKill);

                                if (triple.Any())
                                {
                                    foreach (StormPlayer player in triple)
                                        await FocusPlayer(player, player.When - Timer);
                                }
                                else
                                {
                                    List<StormPlayer> mutli = selector.Select(Analyze.Check(Constants.Heroes.MAX_MULTI_KILL_STREAK_POTENTIAL), SelectorCriteria.MultiKill);

                                    if (mutli.Any())
                                    {
                                        foreach (StormPlayer player in mutli)
                                            await FocusPlayer(player, player.When - Timer);
                                    }
                                    else
                                    {
                                        List<StormPlayer> deaths = selector.Select(Analyze.Check(TimeSpan.FromSeconds(10)), SelectorCriteria.Death);

                                        if (deaths.Any())
                                        {
                                            foreach (StormPlayer player in deaths)
                                                await FocusPlayer(player, player.When - Timer);
                                        }
                                        else
                                        {
                                            List<StormPlayer> mapObjectives = selector.Select(Analyze.Check(TimeSpan.FromSeconds(5)), SelectorCriteria.MapObjective);

                                            if (mapObjectives.Any())
                                            {
                                                foreach (StormPlayer player in mapObjectives)
                                                    await FocusPlayer(player, player.When - Timer);
                                            }
                                            else
                                            {
                                                List<StormPlayer> campObjectives = selector.Select(Analyze.Check(TimeSpan.FromSeconds(10)), SelectorCriteria.CampObjective);

                                                if (campObjectives.Any())
                                                {
                                                    foreach (StormPlayer player in campObjectives)
                                                        await FocusPlayer(player, player.When - Timer);
                                                }
                                                else
                                                {
                                                    List<StormPlayer> structures = selector.Select(Analyze.Check(TimeSpan.FromSeconds(10)), SelectorCriteria.Structure);

                                                    if (structures.Any())
                                                    {
                                                        foreach (var player in structures)
                                                            await FocusPlayer(player, player.When - Timer);
                                                    }
                                                    else
                                                    {
                                                        List<StormPlayer> alive = selector.Select(Analyze.Check(TimeSpan.FromSeconds(5)), SelectorCriteria.Alive);

                                                        if (alive.Any())
                                                        {
                                                            foreach (var player in alive.Shuffle().Take(1))
                                                            {
                                                                if (StormPlayer?.Criteria == SelectorCriteria.Alive && StormPlayer.Player.Team == player.Player.Team) continue;
                                                                await FocusPlayer(player, TimeSpan.FromSeconds(5));
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
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

        private async Task FocusPlayer(StormPlayer stormPlayer, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
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
            while (State != State.EndOfGame && !Token.IsCancellationRequested)
            {
                while (State == State.Running && !Token.IsCancellationRequested)
                {
                    try
                    {
                        AnalyzerResult result = Analyze.Check(TimeSpan.FromSeconds(5));

                        if (result.Talents.Any()) Panel = Panel.Talents;
                        else if (result.TeamObjectives.Any()) Panel = Panel.CarriedObjectives;
                        else if (result.MapObjectives.Any()) Panel = Panel.CarriedObjectives;
                        else if (result.PlayerDeaths.Any()) Panel = Panel.KillsDeathsAssists;
                        else if (result.PlayersAlive.Any())
                        {
                            Panel = panels.IndexOf(Panel) + 1 >= panels.Count ? panels.First() : panels[panels.IndexOf(Panel) + 1];
                        }
                        else
                        {
                            Panel = Panel.KillsDeathsAssists;
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
            foreach(TrackerEvent trackerEvent in StormReplay.Replay.TrackerEvents.Where(e => e.TimeSpan == Timer))
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

            foreach (GameEvent gameEvent in StormReplay.Replay.GameEvents.Where(e => e.TimeSpan == Timer))
            {
                string data = gameEvent.eventType switch
                {
                    GameEventType.CStartGameEvent => $"[{gameEvent.eventType}][{gameEvent.player.Character}][{gameEvent.data}]",
                    GameEventType.CTriggerPingEvent => $"[{gameEvent.eventType}][{gameEvent.player.Character}][{gameEvent.data}]",
                    GameEventType.CUnitClickEvent => $"[{gameEvent.eventType}][{gameEvent.player.Character}][{gameEvent.data}]",
                    GameEventType.CCmdEvent =>  $"[{gameEvent.eventType}][{gameEvent.player.Character}][{gameEvent.data}]",
                    _ => string.Empty,
                };

                if (!string.IsNullOrEmpty(data))
                {
                    logger.LogInformation($"[GameEvent]{data}");
                }
            }

            foreach (Unit unit in StormReplay.Replay.Units.Where(u => u.TimeSpanBorn <= Timer && u.TimeSpanDied.HasValue && u.TimeSpanDied.Value >= Timer))
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
            while (State != State.EndOfGame && !Token.IsCancellationRequested)
            {
                try
                {
                    TimeSpan? elapsed = await heroesOfTheStorm.TryGetTimerAsync();

                    if (elapsed.HasValue)
                    {
                        Timer = elapsed.Value.RemoveNegativeOffset();
                    }

                    if (StormPlayer != null)
                    {
                        logger.LogInformation($"[{StormPlayer.Player.Character}][{StormPlayer.Criteria}][{StormPlayer.When}][{Timer}][{Timer.AddNegativeOffset()}]");
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