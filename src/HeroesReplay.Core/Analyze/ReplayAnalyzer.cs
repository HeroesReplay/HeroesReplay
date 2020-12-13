using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HeroesReplay.Core
{
    public class ReplayAnalyzer : IReplayAnalzer
    {
        private readonly IEnumerable<IFocusCalculator> calculators;
        private readonly ILogger<ReplayAnalyzer> logger;
        private readonly Settings settings;

        public ReplayAnalyzer(ILogger<ReplayAnalyzer> logger, Settings settings, IEnumerable<IFocusCalculator> calculators)
        {
            this.calculators = calculators;
            this.logger = logger;
            this.settings = settings;
        }

        public TimeSpan GetEnd(Replay replay)
        {
            return replay.Units
                .Where(unit => settings.Units.CoreNames.Contains(unit.Name) && unit.TimeSpanDied.HasValue)
                .Min(core => core.TimeSpanDied.Value).Add(settings.Spectate.EndScreenTime);
        }

        public IReadOnlyDictionary<TimeSpan, Panel> GetPanels(Replay replay)
        {
            IDictionary<TimeSpan, Panel> panels = new SortedDictionary<TimeSpan, Panel>();

            if (settings.ParseOptions.ShouldParseUnits)
            {
                foreach (var deathTime in replay.Units.AsParallel().Where(u => u.Group == Unit.UnitGroup.Hero && u.TimeSpanDied.HasValue).GroupBy(x => x.TimeSpanDied.Value))
                {
                    panels[deathTime.Key] = Panel.KillsDeathsAssists;
                }
            }

            if (settings.ParseOptions.ShouldParseStatistics)
            {
                foreach (var talentTime in replay.TeamLevels.SelectMany(x => x).Where(x => settings.Spectate.TalentLevels.Contains(x.Key)).Select(x => x.Value))
                {
                    panels[talentTime] = Panel.Talents;
                }
            }

            return new ReadOnlyDictionary<TimeSpan, Panel>(panels);
        }

        public IReadOnlyDictionary<TimeSpan, Focus> GetPlayers(Replay replay)
        {
            var focusDictionary = new ConcurrentDictionary<TimeSpan, Focus>();
            var timeSpans = Enumerable.Range(0, (int)replay.ReplayLength.TotalSeconds).Select(x => TimeSpan.FromSeconds(x)).ToList();

            timeSpans.AsParallel().ForAll(timeSpan =>
            {
                foreach (Focus focus in calculators.SelectMany(calculator => calculator.GetPlayers(timeSpan, replay)))
                {
                    if (focusDictionary.TryGetValue(timeSpan, out Focus? previous) && previous?.Points < focus.Points)
                    {
                        if (focusDictionary.TryUpdate(timeSpan, focus, previous))
                        {
                            logger.LogInformation($"Updating. Previous: {timeSpan}={previous.Points}. Now: {timeSpan}={focus.Points}");
                        }
                    }
                    else
                    {
                        if (focusDictionary.TryAdd(timeSpan, focus))
                        {
                            logger.LogInformation($"Adding {timeSpan}={focus.Points}");
                        }
                    }
                }
            });

            var processed = new List<Unit>();

            foreach (var entry in focusDictionary.Where(x => x.Value.Points >= settings.Weights.PlayerKill).ToList())
            {
                var currentTime = entry.Key;

                if (processed.Contains(entry.Value.Unit))
                    continue;

                for (int second = 1; second < 10; second++)
                {
                    var pastTime = currentTime.Subtract(TimeSpan.FromSeconds(second));
                    var futureTime = currentTime.Add(TimeSpan.FromSeconds(second));

                    if (focusDictionary.TryGetValue(pastTime, out var past))
                    {
                        if (past.Points < entry.Value.Points)
                        {
                            focusDictionary[pastTime] = entry.Value;
                        }
                    }
                    else
                    {
                        focusDictionary.TryAdd(pastTime, entry.Value);
                    }

                    if (focusDictionary.TryGetValue(futureTime, out var future))
                    {
                        if (entry.Value.Points > future.Points)
                        {
                            focusDictionary[pastTime] = entry.Value;
                        }
                    }
                    else
                    {
                        focusDictionary.TryAdd(futureTime, entry.Value);
                    }
                }

                processed.Add(entry.Value.Unit);
            }

            return new ReadOnlyDictionary<TimeSpan, Focus>(
                new SortedDictionary<TimeSpan, Focus>(
                    focusDictionary.ToDictionary(x => x.Key, x => new Focus(
                        x.Value.Calculator,
                        x.Value.Unit,
                        x.Value.Player,
                        x.Value.Points,
                        x.Value.Description,
                        Array.IndexOf(replay.Players, x.Value.Player))
                    ))
                );
        }

        public bool IsCarriedObjectiveMap(Replay replay) => settings.Maps.CarriedObjectives.Contains(replay.Map);
    }
}