using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml.Automation;
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
                if (panel != value) PanelChange?.Invoke(this, new GameEventArgs<Panel>(StormReplay, value, "Change"));
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
                    HeroChange?.Invoke(this, new GameEventArgs<Player>(StormReplay, value.Player, $"{value.Criteria}: {(value.When - Timer).TotalSeconds}s "));
                }

                stormPlayer = value;
            }
        }

        public State State
        {
            get => state;
            set
            {
                if (state != value) StateChange?.Invoke(this, new GameEventArgs<StateDelta>(StormReplay, new StateDelta(state, value), $"{Timer}"));
                state = value;
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
                if (value != timer) logger.LogInformation($"Timer: {timer}");

                if (value == TimeSpan.Zero) State = State.StartOfGame;
                else if (value >= StormReplay.Replay.ReplayLength) State = State.EndOfGame;
                else if (value > timer) State = State.Running;
                else if (value <= timer) State = State.Paused;

                timer = value;
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
                while (State == State.Running && !Token.IsCancellationRequested)
                {
                    try
                    {
                        List<StormPlayer> penta = selector.Select(Analyze.Check(Constants.Heroes.MAX_PENTA_KILL_STREAK_POTENTIAL), SelectorCriteria.PentaKill);

                        if (penta.Any())
                        {
                            foreach (StormPlayer player in penta)
                            {
                                await FocusPlayer(player);
                            }
                        }
                        else
                        {
                            List<StormPlayer> quad = selector.Select(Analyze.Check(Constants.Heroes.MAX_QUAD_KILL_STREAK_POTENTIAL), SelectorCriteria.QuadKill);

                            if (quad.Any())
                            {
                                foreach (StormPlayer player in quad)
                                {
                                    await FocusPlayer(player);
                                }
                            }
                            else
                            {
                                List<StormPlayer> triple = selector.Select(Analyze.Check(Constants.Heroes.MAX_TRIPLE_KILL_STREAK_POTENTIAL), SelectorCriteria.TripleKill);

                                if (triple.Any())
                                {
                                    foreach (StormPlayer player in triple)
                                    {
                                        await FocusPlayer(player);
                                    }
                                }
                                else
                                {
                                    List<StormPlayer> mutli = selector.Select(Analyze.Check(Constants.Heroes.MAX_MULTI_KILL_STREAK_POTENTIAL), SelectorCriteria.MultiKill);

                                    if (mutli.Any())
                                    {
                                        foreach (StormPlayer player in mutli)
                                        {
                                            await FocusPlayer(player);
                                        }
                                    }
                                    else
                                    {
                                        List<StormPlayer> singles = selector.Select(Analyze.Check(TimeSpan.FromSeconds(10)), SelectorCriteria.Death);

                                        if (singles.Any())
                                        {
                                            foreach (StormPlayer player in singles)
                                            {
                                                await FocusPlayer(player);
                                            }
                                        }
                                        else
                                        {
                                            List<StormPlayer> objectives = selector.Select(Analyze.Check(TimeSpan.FromSeconds(15)), SelectorCriteria.MapObjective, SelectorCriteria.TeamObjective, SelectorCriteria.Structure);

                                            if (objectives.Any())
                                            {
                                                foreach (StormPlayer player in objectives)
                                                {
                                                    await FocusPlayer(player);
                                                }
                                            }
                                            else
                                            {
                                                List<StormPlayer> alive = selector.Select(Analyze.Check(TimeSpan.FromSeconds(5)), SelectorCriteria.Alive);

                                                foreach (var player in alive.OrderBy(rand => Guid.NewGuid()))
                                                {
                                                    await FocusPlayer(player);
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

        private async Task FocusPlayer(StormPlayer stormPlayer)
        {
            if (stormPlayer.Criteria == SelectorCriteria.Alive)
            {
                StormPlayer = stormPlayer;

                await Task.Delay(stormPlayer.When, Token);
            }
            else
            {
                TimeSpan duration = stormPlayer.When - Timer;

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

        private async Task StateLoopAsync()
        {
            while (State != State.EndOfGame && !Token.IsCancellationRequested)
            {
                try
                {
                    TimeSpan? elapsed = await heroesOfTheStorm.TryGetTimerAsync();

                    if (elapsed.HasValue)
                    {
                        Timer = elapsed.Value;
                    }

                    if (StormPlayer != null)
                    {
                        logger.LogInformation($"Focused: {StormPlayer.Player.Character}. Reason: {StormPlayer.Criteria}. Countdown: {(StormPlayer.When - Timer).TotalSeconds}s ");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), Token);
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
        }
    }
}