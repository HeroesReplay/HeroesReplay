using Heroes.ReplayParser;

using HeroesReplay.Core.Runner;
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
                var padding = TimeSpan.FromSeconds(1);

                foreach (var talentTime in replay.TeamLevels.SelectMany(x => x).Where(x => settings.Spectate.TalentLevels.Contains(x.Key)).Select(x => x.Value))
                {
                    panels[talentTime.Subtract(padding)] = Panel.Talents;
                    panels[talentTime] = Panel.Talents;
                    panels[talentTime.Add(padding)] = Panel.Talents;
                }
            }

            return new ReadOnlyDictionary<TimeSpan, Panel>(panels);
        }

        public IReadOnlyDictionary<TimeSpan, Focus> GetPlayers(Replay replay)
        {
            if (replay == null) throw new ArgumentNullException(nameof(replay));

            var focusDictionary = new ConcurrentDictionary<TimeSpan, Focus>();
            var timeSpans = Enumerable.Range(-30, (int)replay.ReplayLength.TotalSeconds).Select(x => TimeSpan.FromSeconds(x)).ToList();

            timeSpans.AsParallel().ForAll(timeSpan =>
            {
                var focuses = new List<Focus>(calculators.SelectMany(calculator => calculator.GetPlayers(timeSpan, replay)));

                foreach (Focus focus in focuses)
                {
                    if (focusDictionary.TryGetValue(timeSpan, out Focus previous) && previous.Points < focus.Points)
                    {
                        if (focusDictionary.TryUpdate(timeSpan, focus, previous))
                        {
                            logger.LogInformation($"Updating. Previous: {timeSpan}={previous.Points}. Now: {timeSpan}={focus.Points}");
                        }
                        else
                        {
                            logger.LogWarning($"Failed to update for {timeSpan}");
                        }
                    }
                    else
                    {
                        if (focusDictionary.TryAdd(timeSpan, focus))
                        {
                            logger.LogInformation($"Adding {timeSpan}={focus.Points}");
                        }
                        else
                        {
                            logger.LogWarning($"Failed to add for {timeSpan} because it already exists");
                        }
                    }
                }
            });

            var processed = new List<Unit>();                      

            const int hisoricalViewerContext = 6;
            const int presentViewerContext = 3;

            foreach (var entry in focusDictionary.Where(x => x.Value.Points >= settings.Weights.PlayerDeath).ToList())
            {
                var currentTime = entry.Key;

                if (processed.Contains(entry.Value.Unit))
                {
                    logger.LogWarning("PlayerDeath viewer 'context' time padding has already been processed. Skipping.");
                    continue;
                }

                for (int second = 1; second < hisoricalViewerContext; second++)
                {
                    var pastTime = currentTime.Subtract(TimeSpan.FromSeconds(second));

                    if (focusDictionary.TryGetValue(pastTime, out var past))
                    {
                        var previousLessWeight = past.Points < entry.Value.Points;

                        if (previousLessWeight)
                        {
                            focusDictionary[pastTime] = entry.Value;
                        }
                    }
                    else focusDictionary.TryAdd(pastTime, entry.Value);
                }

                // Reduce jarring VX when the focus swaps after a kill
                for (int second = 1; second < presentViewerContext; second++)
                {
                    var futureTime = currentTime.Add(TimeSpan.FromSeconds(second));

                    if (focusDictionary.TryGetValue(futureTime, out var future) && future.Unit.TimeSpanDied > futureTime)
                    {
                        var futureWeightedMore = entry.Value.Points > future.Points;

                        if (futureWeightedMore)
                        {
                            focusDictionary[futureTime] = entry.Value;
                        }
                    }
                    else focusDictionary.TryAdd(futureTime, entry.Value);
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

        public TimeSpan GetStart(Replay replay) => replay.TrackerEvents.First(x => x.Data.dictionary[0].blobText == "GatesOpen").TimeSpan;

        public bool IsCarriedObjectiveMap(Replay replay) => replay != null && (settings.Maps.CarriedObjectives.Contains(replay.Map) ||
                                                                               settings.Maps.CarriedObjectives.Contains(replay.MapAlternativeName));
    }
}