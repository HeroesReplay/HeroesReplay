using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Analyzer
{
    public class StormReplayAnalyzer
    {
        private readonly ILogger<StormReplayAnalyzer> logger;

        public StormReplayAnalyzer(ILogger<StormReplayAnalyzer> logger)
        {
            this.logger = logger;
        }

        public AnalyzerResult Analyze(Replay replay, TimeSpan start, TimeSpan end)
        {
            IEnumerable<Player> alive = GetAlivePlayers(start, end, replay);
            IEnumerable<Unit> deaths = GetDeadPlayerUnits(start, end, replay);
            IEnumerable<(Player, TimeSpan)> mapObjectives = GetMapObjectives(start, end, replay, alive);
            IEnumerable<Unit> structures = GetDestroyedStructures(start, end, replay);
            IEnumerable<(int, TimeSpan)> teamTalents = GetTeamTalents(start, end, replay);
            IEnumerable<TeamObjective> teamObjectives = GetTeamObjectives(start, end, replay);
            IEnumerable<GameEvent> pings = GetPings(start, end, alive, replay);
            IEnumerable<(Player, TimeSpan)> killers = GetKillers(deaths, alive);
            IEnumerable<(Player, TimeSpan)> campCaptures = GetCampCaptures(start, end, replay);
            IEnumerable<(Player, TimeSpan)> bossCaptures = GetCampCaptures(start, end, replay, bossCamp: true);
            IEnumerable<Player> allyCore = GetNearAllyCore(start, end, replay);
            IEnumerable<Player> enemyCore = GetNearEnemyCore(start, end, replay);
            IEnumerable<(Player, TimeSpan)> enemyUnits = GetEnemyUnits(start, end, replay);
            IEnumerable<(Player, TimeSpan)> taunts = GetTaunts(start, end, alive, replay);
            IEnumerable<Player> proximity = GetProximity(start, end, alive, replay);

            return new AnalyzerResult(
                replay: replay,
                start: start,
                end: end,
                duration: (end - start),
                deaths: deaths,
                mapObjectives: mapObjectives,
                structures: structures,
                alive: alive,
                allyCore: allyCore,
                enemyCore: enemyCore,
                taunts: taunts,
                proximity: proximity,
                killers: killers,
                pings: pings,
                teamTalents: teamTalents,
                teamObjectives: teamObjectives,
                bossCaptures: bossCaptures,
                campCaptures: campCaptures,
                enemyUnits: enemyUnits
            );
        }

        private IEnumerable<(Player, TimeSpan)> GetTaunts(TimeSpan start, TimeSpan end, IEnumerable<Player> alive, Replay replay)
        {
            foreach (IGrouping<Player, GameEvent> events in replay.GameEvents.Where(e => replay.IsHearthStone(e) && e.TimeSpan.IsWithin(start, end)).GroupBy(e => e.player))
            {
                // May need to also find click events within the same time frame to confirm legitimate bstepping
                var bsteps = events.GroupBy(cmd => cmd.TimeSpan).Where(g => g.Count() > 3);

                if (bsteps.Any() && alive.Contains(events.Key))
                {
                    yield return (events.Key, bsteps.Max(x => x.Key));
                }
            }

            foreach (IGrouping<Player, GameEvent> events in replay.GameEvents.Where(e => replay.IsTaunt(e) && e.TimeSpan.IsWithin(start, end)).GroupBy(e => e.player))
            {
                if (alive.Contains(events.Key))
                {
                    yield return (events.Key, events.Max(t => t.TimeSpan));
                }
            }

            foreach (IGrouping<Player, GameEvent> events in replay.GameEvents.Where(e => replay.IsDance(e) && e.TimeSpan.IsWithin(start, end)).GroupBy(e => e.player))
            {
                if (alive.Contains(events.Key))
                {
                    yield return (events.Key, events.Max(t => t.TimeSpan));
                }
            }
        }

        private IEnumerable<(Player, TimeSpan)> GetKillers(IEnumerable<Unit> deaths, IEnumerable<Player> alive)
        {
            foreach (var death in deaths.Where(death => alive.Contains(death.PlayerKilledBy)))
            {
                yield return (death.PlayerKilledBy, death.TimeSpanDied.Value);
            }
        }

        private IEnumerable<Player> GetProximity(TimeSpan start, TimeSpan end, IEnumerable<Player> alive, Replay replay)
        {
            IEnumerable<Unit> teamOne = alive.SelectMany(p => p.HeroUnits.Where(unit => unit.IsHero() && unit.PlayerControlledBy.Team == 1 && unit.IsAlive(start, end)));
            IEnumerable<Unit> teamTwo = alive.SelectMany(p => p.HeroUnits.Where(unit => unit.IsHero() && unit.PlayerControlledBy.Team == 0 && unit.IsAlive(start, end)));

            foreach (Unit teamOneUnit in teamOne)
            {
                foreach (Unit teamTwoUnit in teamTwo)
                {
                    foreach (Position teamOnePosition in teamOneUnit.Positions.Where(p => p.TimeSpan.IsWithin(start, end)))
                    {
                        foreach (Position teamTwoPosition in teamTwoUnit.Positions.Where(p => p.TimeSpan.IsWithin(start, end)))
                        {
                            double distance = teamOnePosition.Point.DistanceTo(teamTwoPosition.Point);

                            if (distance <= Constants.MAX_DISTANCE_TO_ENEMY)
                            {
                                logger.LogDebug($"distance: {distance}, heroes: {teamOneUnit.PlayerControlledBy.HeroId}, {teamTwoUnit.PlayerControlledBy.HeroId}");

                                yield return teamOneUnit.PlayerControlledBy;
                                yield return teamTwoUnit.PlayerControlledBy;
                            }
                            else
                            {
                                foreach (Unit unit in replay.Units.Where(u => u.IsCapturePoint() || u.IsMapObjective()))
                                {
                                    if (unit.PointBorn.DistanceTo(teamOnePosition.Point) <= Constants.MAX_DISTANCE_TO_OBJECTIVE)
                                    {
                                        yield return teamOneUnit.PlayerControlledBy;
                                    }

                                    if (unit.PointBorn.DistanceTo(teamTwoPosition.Point) <= Constants.MAX_DISTANCE_TO_OBJECTIVE)
                                    {
                                        yield return teamTwoUnit.PlayerControlledBy;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private IEnumerable<(Player, TimeSpan)> GetEnemyUnits(TimeSpan start, TimeSpan end, Replay replay)
        {
            foreach (Unit unit in replay.Units.Where(unit => unit.IsDead(start, end) && unit.IsCamp() && unit.PlayerKilledBy != null))
            {
                logger.LogInformation($"enemy unit: {unit.Name}, killed by: {unit.PlayerKilledBy.HeroId}");
                yield return (unit.PlayerKilledBy, unit.TimeSpanDied.Value);
            }
        }

        private IEnumerable<Player> GetNearAllyCore(TimeSpan start, TimeSpan end, Replay replay)
        {
            foreach (Unit unit in replay.Players.SelectMany(p => p.HeroUnits.Where(unit => unit.IsAlive(start, end))))
            {
                if (unit.Positions.Where(position => position.TimeSpan.IsWithin(start, end)).Any(position => position.Point.DistanceTo(unit.GetSpawn()) < Constants.MAX_DISTANCE_TO_CORE))
                {
                    logger.LogDebug($"near ally core: {unit.PlayerControlledBy.HeroId}");
                    yield return unit.PlayerControlledBy;
                }
            }
        }

        private IEnumerable<Player> GetNearEnemyCore(TimeSpan start, TimeSpan end, Replay replay)
        {
            foreach (Unit unit in replay.Players.SelectMany(p => p.HeroUnits.Where(unit => unit.IsAlive(start, end))))
            {
                if (unit.Positions.Where(position => position.TimeSpan.IsWithin(start, end)).Any(position => position.Point.DistanceTo(unit.GetEnemySpawn(replay)) < Constants.MAX_DISTANCE_TO_CORE))
                {
                    logger.LogDebug($"near enemy core: {unit.PlayerControlledBy.HeroId}");
                    yield return unit.PlayerControlledBy;
                }
            }
        }

        private IEnumerable<Unit> GetDestroyedStructures(TimeSpan start, TimeSpan end, Replay replay)
        {
            return replay.Units.Where(unit => unit.IsDead(start, end) && unit.IsStructure());
        }

        private IEnumerable<(Player, TimeSpan)> GetMapObjectives(TimeSpan start, TimeSpan end, Replay replay, IEnumerable<Player> alive)
        {
            foreach (Unit unit in replay.Units.Where(unit => unit.IsMapObjective()))
            {
                if (unit.IsDead(start, end) && unit.PlayerKilledBy != null && unit.TimeSpanDied.HasValue)
                {
                    yield return (unit.PlayerKilledBy, unit.TimeSpanDied.Value);
                }
                else if (unit.IsDead(start, end) && unit.PlayerKilledBy == null || unit.IsCapturePoint() && unit.OwnerChangeEvents.Any(e => e.TimeSpanOwnerChanged.IsWithin(start, end)))
                {
                    IEnumerable<(Player player, TimeSpan time)> results =
                        from heroUnit in alive.SelectMany(alive => alive.HeroUnits.Where(heroUnit => heroUnit.IsAlive(start, end)))
                        from position in heroUnit.Positions
                        from time in unit.TimeSpanDied.HasValue ? new[] { unit.TimeSpanDied.Value } : unit.OwnerChangeEvents.Select(e => e.TimeSpanOwnerChanged)
                        where time.IsWithin(start, end)
                        where position.TimeSpan.IsWithin(start, time)
                        let distance = position.Point.DistanceTo(unit.PointDied ?? unit.PointBorn)
                        where distance < Constants.MAX_DISTANCE_TO_OWNER_CHANGE
                        orderby distance
                        select (player: heroUnit.PlayerControlledBy, time);

                    foreach ((Player player, TimeSpan time) in results)
                    {
                        yield return (player, time);
                    }
                }
            }
        }

        private IEnumerable<Unit> GetDeadPlayerUnits(TimeSpan start, TimeSpan end, Replay replay)
        {
            return replay.Players.SelectMany(unit => unit.HeroUnits.Where(heroUnit => heroUnit.IsDead(start, end)));
        }

        private IEnumerable<Player> GetAlivePlayers(TimeSpan start, TimeSpan end, Replay replay)
        {
            return replay.Players.Where(player => player.HeroUnits.Any(unit => unit.IsAlive(start, end)));
        }

        private IEnumerable<GameEvent> GetPings(TimeSpan start, TimeSpan end, IEnumerable<Player> alive, Replay replay)
        {
            foreach (GameEvent gameEvent in replay.GameEvents)
            {
                if (gameEvent.eventType == GameEventType.CTriggerPingEvent && gameEvent.TimeSpan.IsWithin(start, end) && alive.Contains(gameEvent.player))
                {
                    yield return gameEvent;
                }
            }
        }

        private IEnumerable<TeamObjective> GetTeamObjectives(TimeSpan start, TimeSpan end, Replay replay)
        {
            foreach (var item in replay.TeamObjectives.SelectMany(teamObjectives => teamObjectives).Where(teamObjective => teamObjective.Player != null && teamObjective.TimeSpan.IsWithin(start, end)))
            {
                logger.LogDebug($"objective: {item.TeamObjectiveType}, player: {item.Player.HeroId}");
                yield return item;
            }
        }

        private IEnumerable<(int, TimeSpan)> GetTeamTalents(TimeSpan start, TimeSpan end, Replay replay)
        {
            return replay.TeamLevels.SelectMany(teamLevels => teamLevels)
                .Where(teamLevel => Constants.TALENT_LEVELS.Contains(teamLevel.Key) && teamLevel.Value.IsWithin(start, end))
                .Select(x => (Team: x.Key, TalentTime: x.Value));
        }

        private IEnumerable<(Player, TimeSpan)> GetCampCaptures(TimeSpan start, TimeSpan end, Replay replay, bool bossCamp = false)
        {
            foreach (TrackerEvent capture in replay.TrackerEvents.GetCampCaptureEvents(start, end))
            {
                int teamId = (int)capture.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value - 1;

                IEnumerable<Unit> campUnit = replay.Units.Where(unit => bossCamp ? unit.IsBossCamp() : unit.IsCamp())
                    .Where(unit => unit.TimeSpanDied.HasValue &&
                                   unit.TimeSpanDied.Value < capture.TimeSpan &&
                                   unit.TimeSpanDied.Value > capture.TimeSpan.Subtract(TimeSpan.FromSeconds(10)) &&
                                   unit.PlayerKilledBy != null && unit.PlayerKilledBy.Team == teamId);

                foreach ((Player player, string name) in campUnit.Select(unit => (unit.PlayerKilledBy, unit.Name)).Distinct())
                {
                    logger.LogDebug($"capture camp: {name}, player: {player.HeroId}");
                    yield return (player, capture.TimeSpan);
                }
            }
        }
    }
}