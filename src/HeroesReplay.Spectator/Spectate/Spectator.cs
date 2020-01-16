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
                    HeroChange?.Invoke(this, new GameEventArgs<Player>(StormReplay, value.Player, $"{value.Reason}: {(value.When - Timer).TotalSeconds}s "));
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
        private readonly CancellationToken token;
        private readonly IStormReplayAnalyzer analyzer;
        private readonly List<Panel> panels = Enum.GetValues(typeof(Panel)).Cast<Panel>().ToList();

        public Spectator(ILogger<Spectator> logger, StormPlayerSelector selector, HeroesOfTheStorm heroesOfTheStorm, CancellationTokenProvider tokenProvider, IStormReplayAnalyzer analyzer)
        {
            this.token = tokenProvider.Token;
            this.logger = logger;
            this.selector = selector;
            this.heroesOfTheStorm = heroesOfTheStorm;
            this.analyzer = analyzer;
        }

        public async Task SpectateAsync(StormReplay stormReplay)
        {
            StormReplay = stormReplay;
            State = State.StartOfGame;
            await Task.WhenAll(Task.Run(PanelLoopAsync, token), Task.Run(FocusLoopAsync, token), Task.Run(StateLoopAsync, token));
        }

        public void Dispose()
        {
            HeroChange = null;
            PanelChange = null;
            StateChange = null;
        }

        private async Task FocusLoopAsync()
        {
            while (State != State.EndOfGame)
            {
                token.ThrowIfCancellationRequested();

                while (State == State.Running)
                {
                    try
                    {
                        AnalyzerResult analyzerResult = Analyze.Seconds(Constants.Heroes.MAX_KILL_STREAK_POTENTIAL.TotalSeconds);

                        foreach (StormPlayer player in selector.Select(analyzerResult))
                        {
                            StormPlayer = player;
                            TimeSpan duration = player.When - Timer;

                            if (duration <= TimeSpan.Zero)
                            {
                                logger.LogInformation($"Focused: {StormPlayer.Player.Character}. Reason: {StormPlayer.Reason}. INVALID DURATION: {duration}.");
                            }
                            else
                            {
                                await Task.Delay(duration, token);
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
            while (State != State.EndOfGame)
            {
                token.ThrowIfCancellationRequested();

                while (State == State.Running)
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

                        await result.Delay(token);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, e.Message);
                    }
                }
            }
        }

        private async Task StateLoopAsync()
        {
            while (State != State.EndOfGame)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    TimeSpan? elapsed = await heroesOfTheStorm.TryGetTimerAsync();
                    if (elapsed.HasValue) Timer = elapsed.Value;

                    if (StormPlayer != null)
                    {
                        logger.LogInformation($"Focused: {StormPlayer.Player.Character}. Reason: {StormPlayer.Reason}. Countdown: {(StormPlayer.When - Timer).TotalSeconds}s ");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), token); // The analyzer checks for 10 seconds into the future, so checking every 5 seconds gives us enough time to analyze with accuracy?
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
        }
    }
}