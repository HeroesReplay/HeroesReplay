using Heroes.ReplayParser;
using HeroesReplay.Core.Shared;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class ReplayAnalyzer : IReplayAnalzer
    {
        private readonly IEnumerable<IGameWeightings> playerWeightings;
        private readonly Settings settings;

        public ReplayAnalyzer(Settings settings, IEnumerable<IGameWeightings> playerWeightings)
        {
            this.playerWeightings = playerWeightings;
            this.settings = settings;
        }

        public IDictionary<TimeSpan, Panel> GetPanels(Replay replay)
        {
            IDictionary<TimeSpan, Panel> panels = new SortedDictionary<TimeSpan, Panel>();

            foreach (var deathTime in replay.Units.AsParallel().Where(u => u.Group == Unit.UnitGroup.Hero && u.TimeSpanDied.HasValue).GroupBy(x => x.TimeSpanDied.Value))
            {
                panels[deathTime.Key] = Panel.KillsDeathsAssists;
            }

            foreach (var talentTime in replay.TeamLevels.SelectMany(x => x).Where(x => settings.SpectateSettings.TalentLevels.Contains(x.Key)).Select(x => x.Value))
            {
                panels[talentTime] = Panel.Talents;
            }

            return panels;
        }

        public IDictionary<TimeSpan, (Player Player, double Points, string Description, int Index)> GetPlayers(Replay replay)
        {
            var focus = new ConcurrentDictionary<TimeSpan, (Unit Unit, Player Player, double Points, string Desc)>();
            var timeSpans = Enumerable.Range(0, (int)replay.ReplayLength.TotalSeconds).Select(x => TimeSpan.FromSeconds(x)).ToList();

            timeSpans.AsParallel().ForAll(timeSpan =>
            {
                foreach (var weighting in playerWeightings.SelectMany(weighter => weighter.GetPlayers(timeSpan, replay)))
                {
                    if (focus.TryGetValue(timeSpan, out var previous) && weighting.Points > previous.Points)
                    {
                        focus[timeSpan] = weighting;
                    }
                    else
                    {
                        focus[timeSpan] = weighting;
                    }
                }
            });

            var processed = new List<Unit>();

            foreach (var entry in focus.Where(x => x.Value.Points >= settings.SpectateWeightSettings.PlayerKill).ToList())
            {
                var currentTime = entry.Key;

                if (processed.Contains(entry.Value.Unit))
                    continue;

                for (int second = 1; second < 10; second++)
                {
                    var pastTime = currentTime.Subtract(TimeSpan.FromSeconds(second));
                    var futureTime = currentTime.Add(TimeSpan.FromSeconds(second));

                    if (focus.TryGetValue(pastTime, out var pastWeighting))
                    {
                        if (pastWeighting.Points < entry.Value.Points)
                        {
                            focus[pastTime] = entry.Value;
                        }
                    }
                    else
                    {
                        focus.TryAdd(pastTime, entry.Value);
                    }

                    if (focus.TryGetValue(futureTime, out var futureWeighting))
                    {
                        if (entry.Value.Points > futureWeighting.Points)
                        {
                            focus[pastTime] = entry.Value;
                        }
                    }
                    else
                    {
                        focus.TryAdd(futureTime, entry.Value);
                    }
                }

                processed.Add(entry.Value.Unit);
            }

            var events = focus.ToDictionary(x => x.Key, x => (x.Value.Player, x.Value.Points, x.Value.Desc, Array.IndexOf(replay.Players, x.Value.Player)));
            return new SortedDictionary<TimeSpan, (Player Player, double Points, string Desc, int Index)>(events);
        }
    }
}