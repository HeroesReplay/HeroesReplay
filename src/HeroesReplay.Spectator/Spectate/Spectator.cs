using System;
using System.Collections.Generic;
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
                        AnalyzerResult analyzerResult = Analyze.Seconds(Constants.Heroes.MAX_KILL_STREAK_POTENTIAL.TotalSeconds);

                        foreach (StormPlayer player in selector.Select(analyzerResult, SelectorCriteria.Any))
                        {
                            TimeSpan duration = player.When - Timer;

                            if (duration <= TimeSpan.Zero || duration.TotalSeconds >= Constants.Heroes.MAX_KILL_STREAK_POTENTIAL.TotalSeconds)
                            {
                                // This tends to happen when the OCR engine recognized result of the 'Timer' is invalid
                                // TODO: Improve OCR by pre-processing the image. (cleaner, contrast, bigger etc)

                                logger.LogInformation($"Focused: {StormPlayer.Player.Character}. Reason: {StormPlayer.Criteria}. INVALID DURATION: {duration}.");
                                continue;
                            }

                            StormPlayer = player;

                            if (player.Criteria == SelectorCriteria.Alive)
                            {
                                await Task.Delay(player.When, Token);
                            }
                            else
                            {
                                await Task.Delay(duration, Token);
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

        private async Task PanelLoopAsync()
        {
            while (State != State.EndOfGame && !Token.IsCancellationRequested)
            {
                while (State == State.Running && !Token.IsCancellationRequested)
                {
                    try
                    {
                        AnalyzerResult result = Analyze.Seconds(20);

                        if (result.Talents.Any()) Panel = Panel.Talents;
                        else if (result.TeamObjectives.Any()) Panel = Panel.CarriedObjectives;
                        else if (result.MapObjectives.Any()) Panel = Panel.CarriedObjectives;
                        else if (result.PlayerDeaths.Any())
                        {
                            Panel = Panel switch
                            {
                                Panel.KillsDeathsAssists => Panel.DeathDamageRole,
                                Panel.Talents => Panel.KillsDeathsAssists,
                                Panel.DeathDamageRole => Panel.KillsDeathsAssists,
                                Panel.ActionsPerMinute => Panel.KillsDeathsAssists,
                                Panel.Experience => Panel.KillsDeathsAssists,
                                Panel.TimeDeadDeathsSelfSustain => Panel.KillsDeathsAssists,
                                Panel.CarriedObjectives => Panel.KillsDeathsAssists,
                                Panel.CrowdControlEnemyHeroes => Panel.KillsDeathsAssists,
                                _ => Panel.DeathDamageRole
                            };
                        }
                        else Panel = panels.IndexOf(Panel) + 1 >= panels.Count ? panels.First() : panels[panels.IndexOf(Panel) + 1];

                        await result.Delay(Token);
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
                    if (elapsed.HasValue) Timer = elapsed.Value;

                    if (StormPlayer != null)
                    {
                        logger.LogInformation($"Focused: {StormPlayer.Player.Character}. Reason: {StormPlayer.Criteria}. Countdown: {(StormPlayer.When - Timer).TotalSeconds}s ");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), Token); // The analyzer checks for 10 seconds into the future, so checking every 5 seconds gives us enough time to analyze with accuracy?
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
        }
    }
}