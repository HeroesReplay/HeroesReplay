using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
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

        public (TimeSpan Timer, Unit Unit, Player Player, String Reason) Focus
        {
            get => focus;
            set
            {
                if (focus.Player != value.Player && value.Player != null) HeroChange?.Invoke(this, new GameEventArgs<Player>(StormReplay, value.Player, value.Reason));

                focus = value;
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
        private (TimeSpan Time, Unit Unit, Player Player, String Reason) focus;

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
        private readonly HeroesOfTheStorm heroesOfTheStorm;
        private readonly IStormReplayAnalyzer analyzer;
        private readonly List<Panel> panels = Enum.GetValues(typeof(Panel)).Cast<Panel>().ToList();
        private readonly CancellationToken token;

        public Spectator(ILogger<Spectator> logger, HeroesOfTheStorm heroesOfTheStorm, IStormReplayAnalyzer analyzer, CancellationTokenSource source)
        {
            this.logger = logger;
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
                        AnalyzerResult result = Analyze.Seconds(10);

                        if (result.Deaths.Any())
                        {
                            foreach (Unit unit in result.Deaths)
                            {
                                var diff = unit.TimeSpanDied.Value - result.Start;
                                Focus = (result.Start, unit, unit.PlayerControlledBy, $"Death in {diff}");
                                await Task.Delay(diff);
                            }
                        }
                        else if (result.MapObjectives.Any())
                        {
                            foreach (var unit in result.MapObjectives)
                            {
                                var diff = unit.TimeSpanDied.Value - result.Start;
                                Focus = (result.Start, null, unit.PlayerKilledBy ?? unit.PlayerControlledBy, $"MapObjective in {diff}");
                                await Task.Delay(unit.TimeSpanDied.Value - result.Start);
                            }
                        }
                        else if (result.TeamObjectives.Any())
                        {
                            foreach (TeamObjective objective in result.TeamObjectives)
                            {
                                var diff = objective.TimeSpan - result.Start;
                                Focus = (result.Start, null, objective.Player, $"TeamObjective in {diff}");
                                await Task.Delay(diff);
                            }
                        }
                        else if (result.Structures.Any())
                        {
                            foreach (Unit unit in result.Structures.Take(2))
                            {
                                var diff = unit.TimeSpanDied.Value - result.Start;
                                Focus = (result.Start, unit, unit.PlayerKilledBy, $"Structure in {diff}");
                                await Task.Delay(diff, token);
                            }
                        }
                        else if (result.Alive.Any())
                        {
                            foreach (Player player in result.Alive.Take(2))
                            {
                                Focus = (result.Start, null, player, "Alive");
                                await Task.Delay(5000, token);
                            }
                        }
                        else
                        {
                            await result.Delay(token);
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