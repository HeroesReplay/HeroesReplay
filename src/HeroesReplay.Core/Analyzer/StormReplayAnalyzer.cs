using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Analyzer
{
    public class StormReplayAnalyzer
    {
        private readonly ILogger<StormReplayAnalyzer> logger;
        private readonly ReplayHelper replayHelper;
        private readonly Settings settings;

        public StormReplayAnalyzer(ILogger<StormReplayAnalyzer> logger, IOptions<Settings> settings, ReplayHelper replayHelper)
        {
            this.logger = logger;
            this.replayHelper = replayHelper;
            this.settings = settings.Value;
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
            IEnumerable<GameEvent> gameEvents = replay.GameEvents.Where(e => replayHelper.IsWithin(e.TimeSpan, start, end));

            foreach (IGrouping<Player, GameEvent> events in gameEvents.Where(e => replayHelper.IsHearthStone(replay, e)).GroupBy(e => e.player))
            {
                // May need to also find click events within the same time frame to confirm legitimate bstepping
                var bsteps = events.GroupBy(cmd => cmd.TimeSpan).Where(g => g.Count() > 3);

                if (bsteps.Any() && alive.Contains(events.Key))
                {
                    yield return (events.Key, bsteps.Max(x => x.Key));
                }
            }

            foreach (IGrouping<Player, GameEvent> events in gameEvents.Where(e => replayHelper.IsTaunt(replay, e)).GroupBy(e => e.player))
            {
                if (alive.Contains(events.Key))
                {
                    yield return (events.Key, events.Max(t => t.TimeSpan));
                }
            }

            foreach (IGrouping<Player, GameEvent> events in gameEvents.Where(e => replayHelper.IsDance(replay, e)).GroupBy(e => e.player))
            {
                if (alive.Contains(events.Key))
                {
                    yield return (events.Key, events.Max(t => t.TimeSpan));
                }
            }
        }

        private IEnumerable<(Player, TimeSpan)> GetKillers(IEnumerable<Unit> deaths, IEnumerable<Player> alive)
        {
            foreach (var death in deaths.Where(death => death.PlayerKilledBy != null && alive.Contains(death.PlayerKilledBy)))
            {
                yield return (death.PlayerKilledBy, death.TimeSpanDied.Value);
            }
        }

        private IEnumerable<Player> GetProximity(TimeSpan start, TimeSpan end, IEnumerable<Player> alive, Replay replay)
        {
            IEnumerable<Unit> teamOne = alive.SelectMany(p => p.HeroUnits.Where(unit => replayHelper.IsHero(unit) && unit.PlayerControlledBy.Team == 1 && replayHelper.IsAlive(unit, start, end)));
            IEnumerable<Unit> teamTwo = alive.SelectMany(p => p.HeroUnits.Where(unit => replayHelper.IsHero(unit) && unit.PlayerControlledBy.Team == 0 && replayHelper.IsAlive(unit, start, end)));

            foreach (Unit teamOneUnit in teamOne)
            {
                foreach (Unit teamTwoUnit in teamTwo)
                {
                    foreach (Position teamOnePosition in teamOneUnit.Positions.Where(p => replayHelper.IsWithin(p.TimeSpan, start, end)))
                    {
                        foreach (Position teamTwoPosition in teamTwoUnit.Positions.Where(p => replayHelper.IsWithin(p.TimeSpan, start, end)))
                        {
                            double distance = teamOnePosition.Point.DistanceTo(teamTwoPosition.Point);

                            // proximity to other players
                            if (distance <= settings.MaxDistanceToEnemy)
                            {
                                logger.LogDebug($"GetProximity: {distance}, heroes: {teamOneUnit.PlayerControlledBy.HeroId}, {teamTwoUnit.PlayerControlledBy.HeroId}");

                                yield return teamOneUnit.PlayerControlledBy;
                                yield return teamTwoUnit.PlayerControlledBy;
                            }
                            else
                            {
                                // beacons, map regen globes etc
                                foreach (Unit unit in replay.Units.Where(u => replayHelper.IsCapturePoint(u) || replayHelper.IsMapObjective(u)))
                                {
                                    if (unit.PointBorn.DistanceTo(teamOnePosition.Point) <= settings.MaxDistanceToObjective)
                                    {
                                        yield return teamOneUnit.PlayerControlledBy;
                                    }

                                    if (unit.PointBorn.DistanceTo(teamTwoPosition.Point) <= settings.MaxDistanceToObjective)
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
            foreach (Unit unit in replay.Units.Where(unit => replayHelper.IsDead(unit, start, end) && replayHelper.IsCamp(unit) && unit.PlayerKilledBy != null))
            {
                logger.LogDebug($"GetEnemyUnits: {unit.Name}, killed by: {unit.PlayerKilledBy.HeroId}");
                yield return (unit.PlayerKilledBy, unit.TimeSpanDied.Value);
            }
        }

        private IEnumerable<Player> GetNearAllyCore(TimeSpan start, TimeSpan end, Replay replay)
        {
            foreach (Unit unit in replay.Players.SelectMany(p => p.HeroUnits.Where(unit => replayHelper.IsAlive(unit, start, end))))
            {
                IEnumerable<Position> positions = unit.Positions.Where(position => replayHelper.IsWithin(position.TimeSpan, start, end));
                bool nearOwnCore = positions.Any(position => position.Point.DistanceTo(replayHelper.GetSpawn(unit)) <= settings.MaxDistanceToCore);

                if (nearOwnCore)
                {
                    logger.LogDebug($"GetNearAllyCore: {unit.PlayerControlledBy.HeroId}");
                    yield return unit.PlayerControlledBy;
                }
            }
        }

        private IEnumerable<Player> GetNearEnemyCore(TimeSpan start, TimeSpan end, Replay replay)
        {
            foreach (Unit unit in replay.Players.SelectMany(p => p.HeroUnits.Where(unit => replayHelper.IsAlive(unit, start, end))))
            {
                IEnumerable<Position> positions = unit.Positions.Where(position => replayHelper.IsWithin(position.TimeSpan, start, end));
                var nearEnemyCore = positions.Any(position => position.Point.DistanceTo(replayHelper.GetEnemySpawn(unit, replay)) <= settings.MaxDistanceToCore);

                if (nearEnemyCore)
                {
                    logger.LogDebug($"GetNearEnemyCore: {unit.PlayerControlledBy.HeroId}");
                    yield return unit.PlayerControlledBy;
                }
            }
        }

        private IEnumerable<Unit> GetDestroyedStructures(TimeSpan start, TimeSpan end, Replay replay)
        {
            return replay.Units.Where(unit => replayHelper.IsDead(unit, start, end) && replayHelper.IsStructure(unit));
        }

        private IEnumerable<(Player, TimeSpan)> GetMapObjectives(TimeSpan start, TimeSpan end, Replay replay, IEnumerable<Player> alive)
        {
            foreach (Unit mapObjective in replay.Units.Where(unit => replayHelper.IsMapObjective(unit)))
            {
                var isDead = replayHelper.IsDead(mapObjective, start, end);

                if (isDead && mapObjective.PlayerKilledBy != null && mapObjective.TimeSpanDied.HasValue)
                {
                    yield return (mapObjective.PlayerKilledBy, mapObjective.TimeSpanDied.Value);
                }
                else if (isDead && mapObjective.PlayerKilledBy == null || replayHelper.IsCapturePoint(mapObjective) && mapObjective.OwnerChangeEvents.Any(e => replayHelper.IsWithin(e.TimeSpanOwnerChanged, start, end)))
                {
                    IEnumerable<(Player player, TimeSpan time)> results =
                        from heroUnit in alive.SelectMany(alive => alive.HeroUnits.Where(heroUnit => replayHelper.IsAlive(heroUnit, start, end)))
                        from position in heroUnit.Positions
                        from time in mapObjective.TimeSpanDied.HasValue ? new[] { mapObjective.TimeSpanDied.Value } : mapObjective.OwnerChangeEvents.Select(e => e.TimeSpanOwnerChanged)
                        where replayHelper.IsWithin(time, start, end)
                        where replayHelper.IsWithin(position.TimeSpan, start, time)
                        let distance = position.Point.DistanceTo(mapObjective.PointDied ?? mapObjective.PointBorn)
                        where distance < settings.MaxDistanceToOwnerChange
                        orderby distance
                        select (player: heroUnit.PlayerControlledBy, time);

                    foreach ((Player player, TimeSpan time) in results)
                    {
                        logger.LogDebug($"GetMapObjectives: {player.HeroId}");

                        yield return (player, time);
                    }
                }
            }
        }

        private IEnumerable<Unit> GetDeadPlayerUnits(TimeSpan start, TimeSpan end, Replay replay)
        {
            return replay.Players.SelectMany(unit => unit.HeroUnits.Where(heroUnit => replayHelper.IsDead(heroUnit, start, end)));
        }

        private IEnumerable<Player> GetAlivePlayers(TimeSpan start, TimeSpan end, Replay replay)
        {
            return replay.Players.Where(player => player.HeroUnits.Any(heroUnit => replayHelper.IsAlive(heroUnit, start, end)));
        }

        private IEnumerable<GameEvent> GetPings(TimeSpan start, TimeSpan end, IEnumerable<Player> alive, Replay replay)
        {
            IEnumerable<GameEvent> pings = replay.GameEvents.Where(ge => ge.eventType == GameEventType.CTriggerPingEvent);

            foreach (GameEvent ping in pings)
            {
                if (replayHelper.IsWithin(ping.TimeSpan, start, end) && alive.Contains(ping.player))
                {
                    yield return ping;
                }
            }
        }

        private IEnumerable<TeamObjective> GetTeamObjectives(TimeSpan start, TimeSpan end, Replay replay)
        {
            foreach (var item in replay.TeamObjectives.SelectMany(teamObjectives => teamObjectives).Where(teamObjective => teamObjective.Player != null && replayHelper.IsWithin(teamObjective.TimeSpan, start, end)))
            {
                logger.LogDebug($"GetTeamObjectives: {item.Player.HeroId}:{item.TeamObjectiveType}");
                yield return item;
            }
        }

        private IEnumerable<(int, TimeSpan)> GetTeamTalents(TimeSpan start, TimeSpan end, Replay replay)
        {
            return replay.TeamLevels.SelectMany(teamLevels => teamLevels)
                .Where(teamLevel => settings.TalentLevels.Contains(teamLevel.Key) && replayHelper.IsWithin(teamLevel.Value, start, end))
                .Select(x => (Team: x.Key, TalentTime: x.Value));
        }

        private IEnumerable<(Player, TimeSpan)> GetCampCaptures(TimeSpan start, TimeSpan end, Replay replay, bool bossCamp = false)
        {
            foreach (TrackerEvent capture in replayHelper.GetCampCaptureEvents(replay.TrackerEvents, start, end))
            {
                int teamId = (int)capture.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value - 1;

                IEnumerable<Unit> campUnit = replay.Units.Where(unit => bossCamp ? replayHelper.IsBossCamp(unit) : replayHelper.IsCamp(unit))
                    .Where(unit => unit.TimeSpanDied.HasValue &&
                                   unit.TimeSpanDied.Value < capture.TimeSpan &&
                                   unit.TimeSpanDied.Value > capture.TimeSpan.Subtract(TimeSpan.FromSeconds(10)) &&
                                   unit.PlayerKilledBy != null && unit.PlayerKilledBy.Team == teamId);

                foreach ((Player player, string name) in campUnit.Select(unit => (unit.PlayerKilledBy, unit.Name)).Distinct())
                {
                    logger.LogDebug($"GetCampCaptures: {player.HeroId}:{name}");

                    yield return (player, capture.TimeSpan);
                }
            }
        }
    }
}