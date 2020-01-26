using HeroesReplay.Shared;

namespace HeroesReplay.Analyzer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Heroes.ReplayParser;

    public class StormReplayAnalyzer
    {
        public AnalyzerResult Analyze(StormReplay stormReplay, TimeSpan start, TimeSpan end)
        {
            Replay replay = stormReplay.Replay;

            IEnumerable<Unit> dead = CalculateDeadUnits(start, end, replay);
            IEnumerable<Player> alive = CalculateAlivePlayers(start, end, replay);
            IEnumerable<Unit> playerDeaths = CalculatePlayerDeaths(dead);
            IEnumerable<Unit> mapObjectives = CalculateMapObjectives(dead);
            IEnumerable<Unit> structures = CalculateStructures(dead);
            IEnumerable<(int Team, TimeSpan TalentTime)> talents = CalculateTalents(start, end, replay);
            IEnumerable<TeamObjective> teamObjectives = CalculateTeamObjectives(start, end, replay);
            IEnumerable<GameEvent> pingSources = CalculatePingSources(stormReplay, start, end, alive);
            IEnumerable<Player> previousKillers = CalculateKillers(CalculatePlayerDeaths(CalculateDeadUnits(start - (start - end), start, replay))).Where(p => alive.Contains(p));
            IEnumerable<(Player, TimeSpan)> camps = CalculateCampCaptures(stormReplay, start, end);

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
                killers: previousKillers,
                camps: camps
            );
        }

        private static IEnumerable<Player> CalculateKillers(IEnumerable<Unit> playerDeaths) => playerDeaths.Select(unit => unit.PlayerKilledBy).Distinct();

        private static IEnumerable<Unit> CalculateStructures(IEnumerable<Unit> dead) => dead.Where(unit => unit.IsStructure());

        private static IEnumerable<Unit> CalculateMapObjectives(IEnumerable<Unit> dead) => dead.Where(unit => unit.IsMapObjective());

        private static IEnumerable<Unit> CalculatePlayerDeaths(IEnumerable<Unit> dead) => dead.Where(unit => unit.IsHero());

        private static IEnumerable<Player> CalculateAlivePlayers(TimeSpan start, TimeSpan end, Replay replay) =>
            replay.Players.Where(player => player.HeroUnits.Any(unit => unit.TimeSpanBorn <= start && unit.TimeSpanDied > end));

        private static IEnumerable<Unit> CalculateDeadUnits(TimeSpan start, TimeSpan end, Replay replay) =>
            replay.Units
                .Where(unit => unit.IsDeadWithin(start, end) && unit.IsPlayerReferenced() && (unit.IsMapObjective() || unit.IsStructure() || unit.IsCamp() || unit.IsHero()))
                .OrderBy(unit => unit.TimeSpanDied);

        /// <summary>
        /// Ping events are only from the team which the replay file originates from
        /// </summary>
        private static IEnumerable<GameEvent> CalculatePingSources(StormReplay stormReplay, TimeSpan start, TimeSpan end, IEnumerable<Player> alive) =>
            stormReplay.Replay.GameEvents.Where(e => e.eventType == GameEventType.CTriggerPingEvent && e.TimeSpan.IsWithin(start, end) && alive.Contains(e.player));

        private static IEnumerable<TeamObjective> CalculateTeamObjectives(TimeSpan start, TimeSpan end, Replay replay) =>
            replay.TeamObjectives
                .SelectMany(teamObjectives => teamObjectives)
                .Where(teamObjective => teamObjective.Player != null && teamObjective.TimeSpan.IsWithin(start, end))
                .OrderBy(objective => objective.TimeSpan);

        private static IEnumerable<(int Team, TimeSpan TalentTime)> CalculateTalents(TimeSpan start, TimeSpan end, Replay replay) =>
            replay.TeamLevels
                .SelectMany(teamLevels => teamLevels)
                .Where(teamLevel => Constants.Heroes.TALENT_LEVELS.Contains(teamLevel.Key))
                .Where(teamLevel => teamLevel.Value.IsWithin(start, end))
                .Select(x => (Team: x.Key, TalentTime: x.Value)).OrderBy(team => team.TalentTime);

        private static IEnumerable<(Player, TimeSpan)> CalculateCampCaptures(StormReplay stormReplay, TimeSpan start, TimeSpan end)
        {
            IEnumerable<TrackerEvent> now = stormReplay.Replay.TrackerEvents.Where(trackerEvent => trackerEvent.TimeSpan.IsWithin(start, end));
            IEnumerable<TrackerEvent> camps = now.Where(trackerEvent => trackerEvent.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent && trackerEvent.Data.dictionary[0].blobText == "JungleCampCapture");

            foreach (TrackerEvent capture in camps)
            {
                int teamId = (int) capture.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value - 1;

                IEnumerable<Unit> mercenaries = stormReplay.Replay.Units.Where(unit => unit.Group == Unit.UnitGroup.MercenaryCamp || unit.Group == Unit.UnitGroup.Unknown && 
                                                                                       unit.TimeSpanDied.HasValue && 
                                                                                       unit.TimeSpanDied.Value > capture.TimeSpan.Subtract(TimeSpan.FromSeconds(30)) && 
                                                                                       unit.TimeSpanDied.Value <= capture.TimeSpan && 
                                                                                       unit.PlayerKilledBy != null && unit.PlayerKilledBy.Team == teamId);
             
                foreach (Player player in mercenaries.Select(unit => unit.PlayerKilledBy).Distinct())
                {
                    yield return (player, capture.TimeSpan);
                }
            }
        }
    }
}