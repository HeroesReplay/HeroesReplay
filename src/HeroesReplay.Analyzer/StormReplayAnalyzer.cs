using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Analyzer
{
    public class StormReplayAnalyzer
    {
        private readonly ILogger<StormReplayAnalyzer> logger;

        public static readonly int[] TALENT_LEVELS = { 1, 4, 7, 10, 13, 16, 20 };

        private const int MAX_DISTANCE_FROM_SPAWN = 30;
        private const int MAX_DISTANCE_TO_ENEMY = 20;

        public StormReplayAnalyzer(ILogger<StormReplayAnalyzer> logger)
        {
            this.logger = logger;
        }

        public AnalyzerResult Analyze(StormReplay stormReplay, TimeSpan start, TimeSpan end)
        {
            Replay replay = stormReplay.Replay;

            IEnumerable<Unit> dead = GetDeadUnits(start, end, replay);
            IEnumerable<Player> alive = GetAlive(start, end, replay);
            IEnumerable<Unit> deaths = GetDead(dead);
            IEnumerable<Unit> mapObjectives = GetMapObjectives(dead);
            IEnumerable<Unit> structures = GetDestroyed(dead);
            IEnumerable<(int, TimeSpan)> talents = GetTalentSelections(start, end, replay);
            IEnumerable<TeamObjective> teamObjectives = GetTeamObjectives(start, end, replay);
            IEnumerable<GameEvent> pings = GetPings(stormReplay, start, end, alive);
            IEnumerable<Player> killers = GetKillers(GetDead(GetDeadUnits(start - (start - end), start, replay))).Where(p => alive.Contains(p));
            IEnumerable<(Player, TimeSpan)> campCaptures = GetCampCaptures(start, end, replay);
            IEnumerable<Player> nearSpawn = GetNearSpawn(start, end, replay);
            IEnumerable<(Player, TimeSpan)> mercenaries = GetMercenaries(dead);
            IEnumerable<Player> dangerZone = GetDangerZone(start, end, alive);

            return new AnalyzerResult(
                stormReplay: stormReplay,
                start: start,
                end: end,
                duration: (end - start),
                deaths: deaths,
                mapObjectives: mapObjectives,
                structures: structures,
                alive: alive,
                nearSpawn: nearSpawn,
                dangerZone: dangerZone,
                killers: killers,
                pings: pings,
                talents: talents,
                teamObjectives: teamObjectives,
                campCaptures: campCaptures,
                mercenaries: mercenaries
            );
        }

        private IEnumerable<Player> GetDangerZone(TimeSpan start, TimeSpan end, IEnumerable<Player> alive)
        {
            var teamOne = alive.SelectMany(p => p.HeroUnits.Where(unit => unit.PlayerControlledBy.Team == 0 && unit.IsAlive(start, end)));
            var teamTwo = alive.SelectMany(p => p.HeroUnits.Where(unit => unit.PlayerControlledBy.Team == 1 && unit.IsAlive(start, end)));

            foreach (Unit teamOneUnit in teamOne)
            {
                foreach (Unit teamTwoUnit in teamTwo)
                {
                    foreach (Position teamOnePosition in teamOneUnit.Positions.Where(p => p.TimeSpan.IsWithin(start, end)))
                    {
                        foreach (Position teamTwoPosition in teamTwoUnit.Positions.Where(p => p.TimeSpan.IsWithin(start, end)))
                        {
                            double distance = teamOnePosition.Point.DistanceTo(teamTwoPosition.Point);

                            if (distance < MAX_DISTANCE_TO_ENEMY)
                            {
                                if (teamOneUnit.IsHeroAbility() || teamTwoUnit.IsHeroAbility()) continue; // Deal with this edge case later

                                logger.LogDebug($"[DANGER][{teamOneUnit.PlayerControlledBy.HeroId}][{teamTwoUnit.PlayerControlledBy.HeroId}][{distance}]");

                                yield return teamOneUnit.PlayerControlledBy;
                                yield return teamTwoUnit.PlayerControlledBy;
                            }
                        }
                    }
                }
            }
        }

        private IEnumerable<(Player, TimeSpan)> GetMercenaries(IEnumerable<Unit> dead)
        {
            foreach (Unit unit in dead)
            {
                if (unit.IsCamp() || unit.Group == Unit.UnitGroup.Unknown)
                {
                    if (unit.PlayerKilledBy != null)
                    {
                        yield return (unit.PlayerKilledBy, unit.TimeSpanDied.Value);
                    }
                }
            }
        }

        private IEnumerable<Player> GetNearSpawn(TimeSpan start, TimeSpan end, Replay replay)
        {
            foreach (Player player in replay.Players)
            {
                foreach (Unit unit in player.HeroUnits.Where(unit => unit.IsAlive(start, end)))
                {
                    if (unit.Positions.Where(position => position.TimeSpan.IsWithin(start, end)).Any(position => position.Point.DistanceTo(unit.GetSpawn()) < MAX_DISTANCE_FROM_SPAWN))
                    {
                        logger.LogDebug($"[{unit.PlayerControlledBy.HeroId}][NearSpawn]");
                        yield return unit.PlayerControlledBy;
                    }
                }
            }
        }

        private static IEnumerable<Player> GetKillers(IEnumerable<Unit> playerDeaths) => playerDeaths.Select(unit => unit.PlayerKilledBy).Distinct();

        private static IEnumerable<Unit> GetDestroyed(IEnumerable<Unit> dead) => dead.Where(unit => unit.IsStructure());

        private static IEnumerable<Unit> GetMapObjectives(IEnumerable<Unit> dead) => dead.Where(unit => unit.IsMapObjective());

        private static IEnumerable<Unit> GetDead(IEnumerable<Unit> dead) => dead.Where(unit => unit.IsHero());

        private IEnumerable<Player> GetAlive(TimeSpan start, TimeSpan end, Replay replay)
        {
            foreach (Player player in replay.Players)
            {
                if (player.HeroUnits.Any(unit => unit.IsAlive(start, end)))
                {
                    // logger.LogDebug($"[{player.HeroId}][Alive]");
                    yield return player;
                }
            }
        }

        private IEnumerable<Unit> GetDeadUnits(TimeSpan start, TimeSpan end, Replay replay)
        {
            List<Unit> list = new List<Unit>();

            foreach (var unit in replay.Units.Where(unit => unit.IsDeadWithin(start, end) && unit.IsPlayerReferenced() && (unit.IsMapObjective() || unit.IsStructure() || unit.IsCamp() || unit.IsHero())).OrderBy(unit => unit.TimeSpanDied))
            {
                logger.LogDebug($"[{unit.Name}][Dies]");
                list.Add(unit);
            }

            return list;
        }

        /// <summary>
        /// Ping events are only from the team which the replay file originates from
        /// </summary>
        private IEnumerable<GameEvent> GetPings(StormReplay stormReplay, TimeSpan start, TimeSpan end, IEnumerable<Player> alive)
        {
            foreach (GameEvent gameEvent in stormReplay.Replay.GameEvents)
            {
                if (gameEvent.eventType == GameEventType.CTriggerPingEvent && gameEvent.TimeSpan.IsWithin(start, end) && alive.Contains(gameEvent.player))
                {
                    yield return gameEvent;
                }
            }
        }

        private IEnumerable<TeamObjective> GetTeamObjectives(TimeSpan start, TimeSpan end, Replay replay)
        {
            foreach (var item in replay.TeamObjectives.SelectMany(teamObjectives => teamObjectives).Where(teamObjective => teamObjective.Player != null && teamObjective.TimeSpan.IsWithin(start, end)).OrderBy(objective => objective.TimeSpan))
            {
                logger.LogDebug($"[TeamObjectives][{item.TeamObjectiveType}][{item.Player.HeroId}]");
                yield return item;
            }
        }

        private IEnumerable<(int, TimeSpan)> GetTalentSelections(TimeSpan start, TimeSpan end, Replay replay)
        {
            return replay.TeamLevels
                .SelectMany(teamLevels => teamLevels)
                .Where(teamLevel => TALENT_LEVELS.Contains(teamLevel.Key))
                .Where(teamLevel => teamLevel.Value.IsWithin(start, end))
                .Select(x => (Team: x.Key, TalentTime: x.Value)).OrderBy(team => team.TalentTime);
        }

        private IEnumerable<(Player, TimeSpan)> GetCampCaptures(TimeSpan start, TimeSpan end, Replay replay)
        {
            IEnumerable<TrackerEvent> range = replay.TrackerEvents.Where(trackerEvent => trackerEvent.TimeSpan.IsWithin(start, end));
            IEnumerable<TrackerEvent> camps = range.Where(trackerEvent => trackerEvent.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent && trackerEvent.Data.dictionary[0].blobText == "JungleCampCapture");

            foreach (TrackerEvent capture in camps)
            {
                int teamId = (int)capture.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value - 1;

                IEnumerable<Unit> mercenaries = replay.Units.Where(unit => unit.Group == Unit.UnitGroup.MercenaryCamp || unit.Group == Unit.UnitGroup.Unknown &&
                                                                           unit.TimeSpanDied.HasValue && unit.TimeSpanDied.Value < end &&
                                                                           capture.TimeSpan.Subtract(TimeSpan.FromSeconds(10)) < unit.TimeSpanDied.Value &&
                                                                           capture.TimeSpan > unit.TimeSpanDied.Value &&
                                                                           unit.PlayerKilledBy != null && unit.PlayerKilledBy.Team == teamId);

                foreach (Player player in mercenaries.Where(unit => unit.PlayerKilledBy != null).Select(unit => unit.PlayerKilledBy).Distinct())
                {
                    yield return (player, capture.TimeSpan);
                }
            }
        }
    }
}