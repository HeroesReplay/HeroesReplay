using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;

namespace HeroesReplay.Spectator
{
    public class StormReplayAnalyzer
    {
        

        public AnalyzerResult Analyze(StormReplay stormReplay, TimeSpan start, TimeSpan end)
        {
            Replay replay = stormReplay.Replay;

            List<Unit> dead = replay.Units.Where(unit => unit.DiesWithin(start, end) && unit.IsPlayerReferenced() && (unit.IsMapObjective() || unit.IsStructure() || unit.IsCamp() || unit.IsHero())).OrderBy(unit => unit.TimeSpanDied).ToList();
            List<Player> alive = replay.Players.Where(player => !player.HeroUnits.Any(unit => unit.DiesWithin(start, end)) && player.HeroUnits.Any(unit => unit.TimeSpanBorn <= start && unit.TimeSpanDied > end)).ToList();

            List<Unit> structures = dead.Where(unit => unit.IsStructure()).ToList();

            List<(int Team, TimeSpan TalentTime)> talents = replay.TeamLevels.SelectMany(teamLevels => teamLevels)
                .Where(teamLevel => Constants.Heroes.TALENT_LEVELS.Contains(teamLevel.Key))
                .Where(teamLevel => teamLevel.Value.IsWithin(start, end))
                .Select(x => (Team: x.Key, TalentTime: x.Value)).OrderBy(team => team.TalentTime).ToList();

            List<TeamObjective> teamObjectives = replay.TeamObjectives.SelectMany(teamObjectives => teamObjectives)
                .Where(teamObjective => teamObjective.Player != null && teamObjective.TimeSpan.IsWithin(start, end))
                .OrderBy(objective => objective.TimeSpan).ToList();

            List<GameEvent> pingSources = stormReplay.Replay.GameEvents.Where(e => e.eventType == GameEventType.CTriggerPingEvent && e.TimeSpan.IsWithin(start, end) && alive.Contains(e.player)).ToList();
            

            // TODO: add players that recently killed other players (focus them instead of other alive players)

            return new AnalyzerResult(
                stormReplay: stormReplay,
                start: start,
                end: end,
                duration: (end - start),
                playerDeaths: dead.Where(unit => unit.IsHero()).ToList(),
                mapObjectives: dead.Where(unit => unit.IsMapObjective()).ToList(),
                structures: structures,
                playersAlive: alive,
                pings: pingSources,
                talents: talents,
                teamObjectives: teamObjectives
            );
        }
    }
}