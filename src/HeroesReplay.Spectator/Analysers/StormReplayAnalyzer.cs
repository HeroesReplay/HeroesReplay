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

            List<Unit> dead = CalculateDeadUnits(start, end, replay);
            List<Player> alive = CalculateAlivePlayers(start, end, replay);
            List<Unit> playerDeaths = CalculatePlayerDeaths(dead);
            List<Unit> mapObjectives = CalculateMapObjectives(dead);
            List<Unit> structures = CalculateStructures(dead);
            List<(int Team, TimeSpan TalentTime)> talents = CalculateTalents(start, end, replay);
            List<TeamObjective> teamObjectives = CalculateTeamObjectives(start, end, replay);
            List<GameEvent> pingSources = CalculatePingSources(stormReplay, start, end, alive);
            List<Player> killers = CalculateKillers(playerDeaths);
            List<(Player, TimeSpan)> camps = CalculateCampCaptures(stormReplay, start, end);

            return new AnalyzerResult(
                stormReplay: stormReplay,
                start: start,
                end: end,
                duration: (end - start),
                playerDeaths: playerDeaths,
                mapObjectives: mapObjectives,
                structures: structures,
                playersAlive: alive,
                pingSources: pingSources,
                talents: talents,
                teamObjectives: teamObjectives,
                killers: killers,
                camps: camps
            );
        }

        private static List<(Player, TimeSpan)> CalculateCampCaptures(StormReplay stormReplay, TimeSpan start, TimeSpan end)
        {
            List<(Player, TimeSpan)> captures = new List<(Player, TimeSpan)>();

            IEnumerable<TrackerEvent> campEvents = stormReplay.Replay.TrackerEvents.Where(e =>
                e.TimeSpan.IsWithin(start, end) &&
                e.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent &&
                e.Data.dictionary[0].blobText == "JungleCampCapture");

            foreach (var capture in campEvents)
            {
                long teamId = capture.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value - 1;

                IEnumerable<Unit> units = stormReplay.Replay.Units.Where(unit =>
                    unit.Group == Unit.UnitGroup.MercenaryCamp &&
                    unit.PlayerKilledBy != null && 
                    unit.Team.HasValue && unit.Team.Value == teamId &&
                    unit.TimeSpanDied > capture.TimeSpan.Subtract(TimeSpan.FromSeconds(30)) &&
                    unit.TimeSpanDied < capture.TimeSpan.Add(TimeSpan.FromSeconds(30)));

                foreach (Player player in units.Select(unit => unit.PlayerKilledBy).Distinct())
                {
                    captures.Add((player, units.Max(u => u.TimeSpanDied.Value)));
                }
            }

            return captures;
        }

        private static List<Player> CalculateKillers(List<Unit> playerDeaths) => playerDeaths.Select(unit => unit.PlayerKilledBy).Distinct().ToList();

        private static List<Unit> CalculateStructures(List<Unit> dead) => dead.Where(unit => unit.IsStructure()).ToList();

        private static List<Unit> CalculateMapObjectives(List<Unit> dead) => dead.Where(unit => unit.IsMapObjective()).ToList();

        private static List<Unit> CalculatePlayerDeaths(List<Unit> dead) => dead.Where(unit => unit.IsHero()).ToList();

        private static List<Player> CalculateAlivePlayers(TimeSpan start, TimeSpan end, Replay replay)
        {
            return replay.Players
                .Where(player => !player.HeroUnits.Any(unit => unit.DiesWithin(start, end)) && player.HeroUnits.Any(unit => unit.TimeSpanBorn <= start && unit.TimeSpanDied > end))
                .ToList();
        }

        private static List<Unit> CalculateDeadUnits(TimeSpan start, TimeSpan end, Replay replay)
        {
            return replay.Units
                .Where(unit => unit.DiesWithin(start, end) && unit.IsPlayerReferenced() && (unit.IsMapObjective() || unit.IsStructure() || unit.IsCamp() || unit.IsHero()))
                .OrderBy(unit => unit.TimeSpanDied).ToList();
        }

        private static List<GameEvent> CalculatePingSources(StormReplay stormReplay, TimeSpan start, TimeSpan end, List<Player> alive)
        {
            return stormReplay.Replay.GameEvents.Where(e =>
                e.eventType == GameEventType.CTriggerPingEvent && e.TimeSpan.IsWithin(start, end) &&
                alive.Contains(e.player)).ToList();
        }

        private static List<TeamObjective> CalculateTeamObjectives(TimeSpan start, TimeSpan end, Replay replay)
        {
            return replay.TeamObjectives.SelectMany(teamObjectives => teamObjectives).Where(teamObjective => teamObjective.Player != null && teamObjective.TimeSpan.IsWithin(start, end)).OrderBy(objective => objective.TimeSpan).ToList();
        }

        private static List<(int Team, TimeSpan TalentTime)> CalculateTalents(TimeSpan start, TimeSpan end, Replay replay)
        {
            return replay.TeamLevels.SelectMany(teamLevels => teamLevels)
                .Where(teamLevel => Constants.Heroes.TALENT_LEVELS.Contains(teamLevel.Key))
                .Where(teamLevel => teamLevel.Value.IsWithin(start, end))
                .Select(x => (Team: x.Key, TalentTime: x.Value)).OrderBy(team => team.TalentTime).ToList();
        }
    }
}