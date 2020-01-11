using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;

namespace HeroesReplay
{
    public sealed class DefaultStormReplayAnalyzer : IStormReplayAnalyzer
    {
        private static readonly int[] TalentLevels = { 1, 4, 7, 10, 13, 16, 20 };

        public AnalyzerResult Analyze(StormReplay stormReplay, TimeSpan start, TimeSpan end)
        {
            Replay replay = stormReplay.Replay;

            List<Unit> units = replay.Units.Where(unit => unit.IsTimeSpanDiedWithin(start, end) && unit.IsPlayerReferenced() && (unit.IsMapObjective() ||  unit.IsStructure() || unit.IsCamp() || unit.IsHero())).ToList();

            return new AnalyzerResult(
                stormReplay: stormReplay,
                start: start,
                end: end,
                deaths: units.Where(unit => unit.IsHero()).ToList(),
                mapObjectives: units.Where(unit => unit.IsMapObjective()).ToList(),
                structures: units.Where(unit => unit.IsStructure()).ToList(),
                alive: replay.Players.Where(player => !player.HeroUnits.Any(unit => unit.IsTimeSpanDiedWithin(start, end))).ToList(),
                talents: replay.TeamLevels.SelectMany(teamLevels => teamLevels).Where(teamLevel => TalentLevels.Contains(teamLevel.Key)).Where(teamLevel => teamLevel.Value.IsWithin(start, end)).Select(x => (Team: x.Key, TalentTime: x.Value)).OrderBy(team => team.TalentTime).ToList(),
                teamObjectives: replay.TeamObjectives.SelectMany(teamObjectives => teamObjectives).Where(teamObjective => teamObjective.TimeSpan.IsWithin(start, end) && teamObjective.Player != null).OrderBy(objective => objective.TimeSpan).ToList()
            );
        }
    }
}