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
using HeroesReplay.Core.Services.HeroesProfile;

namespace HeroesReplay.Core
{
    public class ReplayAnalyzer : IReplayAnalyzer
    {
        private readonly IEnumerable<IFocusCalculator> calculators;
        private readonly IGameData gameData;
        private readonly ILogger<ReplayAnalyzer> logger;
        private readonly AppSettings settings;
        private readonly IExtensionPayloadsBuilder payloadsBuilder;

        public ReplayAnalyzer(ILogger<ReplayAnalyzer> logger, AppSettings settings, IExtensionPayloadsBuilder payloadsBuilder, IEnumerable<IFocusCalculator> calculators, IGameData gameData)
        {
            this.calculators = calculators ?? throw new ArgumentNullException(nameof(calculators));
            this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.payloadsBuilder = payloadsBuilder ?? throw new ArgumentNullException(nameof(payloadsBuilder));
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
                foreach (var deathTime in replay.Players.SelectMany(x => x.HeroUnits).Where(u => u.TimeSpanDied.HasValue).GroupBy(x => x.TimeSpanDied.GetValueOrDefault()))
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

        public ITalentPayloads GetPayloads(Replay replay)
        {
            return payloadsBuilder.CreatePayloads(replay);
        }

        public IReadOnlyDictionary<TimeSpan, Focus> GetPlayers(Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            var focusDictionary = new ConcurrentDictionary<TimeSpan, Focus>();
            var timeSpans = Enumerable.Range(TimeSpan.Zero.Seconds, Convert.ToInt32(replay.ReplayLength.TotalSeconds)).Select(second => TimeSpan.FromSeconds(second)).ToList();

            timeSpans
                .AsParallel()
                .ForAll(second =>
                {
                    foreach (var focus in calculators.SelectMany(calculator => calculator.GetFocusPlayers(second, replay)))
                    {
                        if (!focusDictionary.ContainsKey(second))
                        {
                            logger.LogTrace($"Added: {focus.Description}");
                            focusDictionary[second] = focus;
                        }
                        else if (focusDictionary.ContainsKey(second) && focusDictionary[second].Points < focus.Points)
                        {
                            logger.LogTrace($"Updated: {focus.Description}");
                            focusDictionary[second] = focus;
                        }
                    }
                });

            logger.LogInformation($"focus count: {focusDictionary.Keys.Count}");

            var unitDied = new List<Unit>();

            var deathEntries = focusDictionary.Where(kv => kv.Value.Calculator == typeof(KillCalculator) || kv.Value.Calculator == typeof(DeathCalculator)).ToList();

            foreach (var entry in deathEntries)
            {
                var currentTime = entry.Key;

                if (unitDied.Contains(entry.Value.Unit))
                {
                    logger.LogWarning("Death viewer 'context' has already been processed. Skipping.");
                    continue;
                }

                for (int second = 1; second < settings.Spectate.PastDeathContextTime.TotalSeconds; second++)
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
                    else
                    {
                        focusDictionary.TryAdd(pastTime, entry.Value);
                    }
                }

                // Reduce jarring VX when the focus swaps after a kill
                for (int second = 1; second < settings.Spectate.PresentDeathContextTime.TotalSeconds; second++)
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
                    else
                    {
                        focusDictionary.TryAdd(futureTime, entry.Value);
                    }
                }

                unitDied.Add(entry.Value.Unit);
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

        public TimeSpan GetStart(Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            return replay.TrackerEvents.First(x => x.Data.dictionary[0].blobText == settings.TrackerEvents.GatesOpen).TimeSpan;
        }

        public bool GetIsCarriedObjective(Replay replay) => replay != null && (settings.Maps.CarriedObjectives.Contains(replay.Map) ||
                                                                               settings.Maps.CarriedObjectives.Contains(replay.MapAlternativeName));
    }
}