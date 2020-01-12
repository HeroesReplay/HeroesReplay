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

            List<Unit> dead = replay.Units.Where(unit => unit.DiesWithin(start, end) && unit.IsPlayerReferenced() && (unit.IsMapObjective() ||  unit.IsStructure() || unit.IsCamp() || unit.IsHero())).ToList();
            List<Player> alive = replay.Players.Where(player => !player.HeroUnits.Any(unit => unit.DiesWithin(start, end)) && player.HeroUnits.Any(unit => unit.TimeSpanBorn <= start && unit.TimeSpanDied > end)).ToList();

            List<Unit> structures = dead.Where(unit => unit.IsStructure()).ToList();

            List<(int Team, TimeSpan TalentTime)> talents = replay.TeamLevels.SelectMany(teamLevels => teamLevels)
                .Where(teamLevel => TalentLevels.Contains(teamLevel.Key))
                .Where(teamLevel => teamLevel.Value.IsWithin(start, end))
                .Select(x => (Team: x.Key, TalentTime: x.Value)).OrderBy(team => team.TalentTime).ToList();

            List<TeamObjective> teamObjectives = replay.TeamObjectives.SelectMany(teamObjectives => teamObjectives)
                .Where(teamObjective => teamObjective.TimeSpan.IsWithin(start, end) && teamObjective.Player != null)
                .OrderBy(objective => objective.TimeSpan).ToList();

            // TODO: add players that recently killed other players (focus them instead of other alive players)

            return new AnalyzerResult(
                stormReplay: stormReplay,
                start: start,
                end: end,
                playerDeaths: dead.Where(unit => unit.IsHero()).ToList(),
                mapObjectives: dead.Where(unit => unit.IsMapObjective()).ToList(),
                structures: structures,
                playersAlive: alive,
                talents: talents,
                teamObjectives: teamObjectives
            );
        }
    }
}