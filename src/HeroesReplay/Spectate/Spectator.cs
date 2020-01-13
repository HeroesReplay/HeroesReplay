using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Selector;
using Microsoft.Extensions.Logging;

namespace HeroesReplay
{
    public sealed class Spectator : IDisposable
    {
        public event EventHandler<GameEventArgs<Player>> HeroChange;
        public event EventHandler<GameEventArgs<Panel>> PanelChange;
        public event EventHandler<GameEventArgs<StateDelta>> StateChange;

        public static readonly GameMode[] SupportedModes = { GameMode.Brawl, GameMode.HeroLeague, GameMode.QuickMatch, GameMode.UnrankedDraft };

        public Panel Panel
        {
            get => panel;
            set
            {
                if (panel != value) PanelChange?.Invoke(this, new GameEventArgs<Panel>(StormReplay, value, "Change"));
                panel = value;
            }
        }

        public PlayerSelect Player
        {
            get => player;
            set
            {
                if (player.Player != value.Player)
                {
                    HeroChange?.Invoke(this, new GameEventArgs<Player>(StormReplay, value.Player, value.Reason.ToString()));
                }

                player = value;
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
        private PlayerSelect player;

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
        private readonly PrioritySelector prioritySelector;
        private readonly HeroesOfTheStorm heroesOfTheStorm;
        private readonly IStormReplayAnalyzer analyzer;
        private readonly List<Panel> panels = Enum.GetValues(typeof(Panel)).Cast<Panel>().ToList();
        private readonly CancellationToken token;

        public Spectator(ILogger<Spectator> logger, PrioritySelector prioritySelector, HeroesOfTheStorm heroesOfTheStorm, IStormReplayAnalyzer analyzer, CancellationTokenSource source)
        {
            this.logger = logger;
            this.prioritySelector = prioritySelector;
            this.heroesOfTheStorm = heroesOfTheStorm;
            this.analyzer = analyzer;
            this.token = source.Token;
        }

        public async Task SpectateAsync(StormReplay stormReplay)
        {
            StormReplay = stormReplay;
            State = State.StartOfGame;

            await Task.WhenAll(Task.Run(PanelLoopAsync, token), Task.Run(FocusLoopAsync, token), Task.Run(StateLoopAsync, token));
        }

        public void Dispose()
        {

        }

        private async Task FocusLoopAsync()
        {
            while (!token.IsCancellationRequested && State != State.EndOfGame)
            {
                while (State == State.Running && !token.IsCancellationRequested)
                {
                    try
                    {
                        foreach (PlayerSelect playerSelect in prioritySelector.Prioritize(Analyze.Seconds(10)))
                        {
                            Player = playerSelect;
                            await Player.WatchAsync();
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
            while (!token.IsCancellationRequested && State != State.EndOfGame)
            {
                while (State == State.Running && !token.IsCancellationRequested)
                {
                    try
                    {
                        AnalyzerResult result = Analyze.Seconds(20);

                        if (result.Talents.Any()) Panel = Panel.Talents;
                        else if (result.TeamObjectives.Any()) Panel = Panel.CarriedObjectives;
                        else if (result.MapObjectives.Any()) Panel = Panel.CarriedObjectives;
                        else if (result.Deaths.Any())
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
            while (!token.IsCancellationRequested && State != State.EndOfGame)
            {
                try
                {
                    TimeSpan? elapsed = await heroesOfTheStorm.TryGetTimerAsync();
                    if (elapsed.HasValue) Timer = elapsed.Value;

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