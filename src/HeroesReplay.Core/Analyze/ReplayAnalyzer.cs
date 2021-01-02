using Heroes.ReplayParser;

using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HeroesReplay.Core.Configuration;

namespace HeroesReplay.Core
{
    public class ReplayAnalyzer : IReplayAnalzer
    {
        private readonly IEnumerable<IFocusCalculator> calculators;
        private readonly IGameData gameData;
        private readonly ILogger<ReplayAnalyzer> logger;
        private readonly AppSettings settings;

        public ReplayAnalyzer(ILogger<ReplayAnalyzer> logger, AppSettings settings, IEnumerable<IFocusCalculator> calculators, IGameData gameData)
        {
            this.calculators = calculators;
            this.gameData = gameData;
            this.logger = logger;
            this.settings = settings;
        }

        public TimeSpan GetEnd(Replay replay)
        {
            if (replay == null) 
                throw new ArgumentNullException(nameof(replay));

            return replay.Units
                .Where(unit => gameData.CoreUnits.Contains(unit.Name) && unit.TimeSpanDied.HasValue)
                .Min(core => core.TimeSpanDied.GetValueOrDefault());
        }

        public IReadOnlyDictionary<TimeSpan, Panel> GetPanels(Replay replay)
        {
            if (replay == null) throw new ArgumentNullException(nameof(replay));

            IDictionary<TimeSpan, Panel> panels = new SortedDictionary<TimeSpan, Panel>();

            if (settings.ParseOptions.ShouldParseUnits)
            {
                foreach (var deathTime in replay.Units.AsParallel().Where(u => Unit.UnitGroup.Hero.Equals(gameData.GetUnitGroup(u.Name)) && u.TimeSpanDied.HasValue).GroupBy(x => x.TimeSpanDied.GetValueOrDefault()))
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
            if (replay == null) throw new ArgumentNullException(nameof(replay));

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

            foreach (var entry in focusDictionary.Where(x => x.Value.Points >= settings.Weights.PlayerDeath).ToList())
            {
                var currentTime = entry.Key;

                if (processed.Contains(entry.Value.Unit))
                    continue;

                for (int second = 1; second < 6; second++)
                {
                    var pastTime = currentTime.Subtract(TimeSpan.FromSeconds(second));

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

                    var futureTime = currentTime.Add(TimeSpan.FromSeconds(second));

                    if (focusDictionary.TryGetValue(futureTime, out var future) && future.Unit.TimeSpanDied > futureTime)
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
                        x.Value.Target,
                        x.Value.Points,
                        x.Value.Description,
                        Array.IndexOf(replay.Players, x.Value.Target))
                    ))
                );
        }

        public bool IsCarriedObjectiveMap(Replay replay) => replay != null && (settings.Maps.CarriedObjectives.Contains(replay.Map) ||
                                                                               settings.Maps.CarriedObjectives.Contains(replay.MapAlternativeName));
    }
}