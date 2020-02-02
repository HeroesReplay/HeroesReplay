using HeroesReplay.Analyzer;
using HeroesReplay.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;

namespace HeroesReplay.Spectator
{
    public class StormPlayerTool
    {
        private readonly ILogger<StormPlayerTool> logger;
        private readonly StormReplayAnalyzer analyzer;

        private const int MAX_DISTANCE_OWNER_CHANGE = 8;

        public StormPlayerTool(ILogger<StormPlayerTool> logger, StormReplayAnalyzer analyzer)
        {
            this.logger = logger;
            this.analyzer = analyzer;
        }

        public List<StormPlayer> GetPlayers(StormReplay stormReplay, TimeSpan timer)
        {
            IEnumerable<StormPlayer> pentaKills = Select(analyzer.Analyze(stormReplay, timer, timer.Add(Constants.MAX_PENTA_KILL_STREAK_POTENTIAL)), GameEvent.PentaKill);
            IEnumerable<StormPlayer> quadKills = Select(analyzer.Analyze(stormReplay, timer, timer.Add(Constants.MAX_QUAD_KILL_STREAK_POTENTIAL)), GameEvent.QuadKill);
            IEnumerable<StormPlayer> tripleKills = Select(analyzer.Analyze(stormReplay, timer, timer.Add(Constants.MAX_TRIPLE_KILL_STREAK_POTENTIAL)), GameEvent.TripleKill);
            IEnumerable<StormPlayer> mutikills = Select(analyzer.Analyze(stormReplay, timer, timer.Add(Constants.MAX_MULTI_KILL_STREAK_POTENTIAL)), GameEvent.MultiKill);
            IEnumerable<StormPlayer> killers = Select(analyzer.Analyze(stormReplay, timer, timer.Add(Constants.KILL_STREAK_TIMER)), GameEvent.Kill);
            IEnumerable<StormPlayer> deaths = Select(analyzer.Analyze(stormReplay, timer, timer.Add(Constants.KILL_STREAK_TIMER)), GameEvent.Death);
            IEnumerable<StormPlayer> previousKillers = Select(analyzer.Analyze(stormReplay, timer, timer.Add(TimeSpan.FromSeconds(10))), GameEvent.KilledEnemy);
            IEnumerable<StormPlayer> mapObjectives = Select(analyzer.Analyze(stormReplay, timer, timer.Add(TimeSpan.FromSeconds(10))), GameEvent.MapObjective);
            IEnumerable<StormPlayer> teamObjectives = Select(analyzer.Analyze(stormReplay, timer, timer.Add(TimeSpan.FromSeconds(10))), GameEvent.TeamObjective);
            IEnumerable<StormPlayer> camps = Select(analyzer.Analyze(stormReplay, timer, timer.Add(TimeSpan.FromSeconds(10))), GameEvent.MercenaryCamp);
            IEnumerable<StormPlayer> mercenaries = Select(analyzer.Analyze(stormReplay, timer, timer.Add(TimeSpan.FromSeconds(5))), GameEvent.MercenaryKill);
            IEnumerable<StormPlayer> proximity = Select(analyzer.Analyze(stormReplay, timer, timer.Add(TimeSpan.FromSeconds(5))), GameEvent.EnemyProximity);
            IEnumerable<StormPlayer> structures = Select(analyzer.Analyze(stormReplay, timer, timer.Add(TimeSpan.FromSeconds(5))), GameEvent.Structure);
            IEnumerable<StormPlayer> alivePlayers = Select(analyzer.Analyze(stormReplay, timer, timer.Add(TimeSpan.FromSeconds(5))), GameEvent.Alive);

            return pentaKills.Or(quadKills.Or(tripleKills.Or(mutikills.Or(killers.Or(deaths.Or(mapObjectives.Or(camps.Or(mercenaries.Or(teamObjectives.Or(structures.Or(proximity.Or(previousKillers.Or(alivePlayers))))))))))))).ToList();
        }

        private IEnumerable<StormPlayer> Select(AnalyzerResult result, GameEvent gameEvent)
        {
            IEnumerable<StormPlayer> players = gameEvent switch
            {
                GameEvent.PentaKill => HandleKills(result, GameEvent.PentaKill),
                GameEvent.QuadKill => HandleKills(result, GameEvent.QuadKill),
                GameEvent.TripleKill => HandleKills(result, GameEvent.TripleKill),
                GameEvent.MultiKill => HandleKills(result, GameEvent.MultiKill),
                GameEvent.Kill => HandleKills(result, GameEvent.Kill),
                GameEvent.Death => HandleDeaths(result),
                GameEvent.MapObjective => HandleMapObjectives(result),
                GameEvent.TeamObjective => HandleTeamObjectives(result),
                GameEvent.MercenaryCamp => HandleCampCaptures(result),
                GameEvent.MercenaryKill => HandleCampKills(result),
                GameEvent.Structure => HandleStructures(result),
                GameEvent.KilledEnemy => HandlePreviousKillers(result),
                GameEvent.EnemyProximity => HandleProximity(result),
                GameEvent.Alive => HandleAlive(result),
                GameEvent.Ping => HandlePings(result),
                _ => throw new ArgumentOutOfRangeException(nameof(gameEvent), gameEvent, null)
            };

            return players.OrderBy(p => p.Duration);
        }

        private IEnumerable<StormPlayer> HandleProximity(AnalyzerResult result)
        {
            foreach (Player player in result.Proximity)
            {
                yield return new StormPlayer(player, result.Start, result.Duration, GameEvent.EnemyProximity);
            }
        }

        private IEnumerable<StormPlayer> HandleCampKills(AnalyzerResult result)
        {
            foreach ((Player Player, TimeSpan TimeDied) unit in result.Mercenaries)
            {
                yield return new StormPlayer(unit.Player, result.Start, unit.TimeDied, GameEvent.MercenaryKill);
            }
        }

        private IEnumerable<StormPlayer> HandlePreviousKillers(AnalyzerResult result)
        {
            foreach (Player killer in result.Killers)
            {
                yield return new StormPlayer(killer, result.Start, result.Duration, GameEvent.KilledEnemy);
            }
        }

        private IEnumerable<StormPlayer> HandleDeaths(AnalyzerResult result)
        {
            foreach (Unit death in result.Deaths)
            {
                yield return new StormPlayer(death.PlayerControlledBy, result.Start, death.TimeSpanDied.Value.Add(TimeSpan.FromSeconds(2)), GameEvent.Death);
            }
        }

        private IEnumerable<StormPlayer> HandlePings(AnalyzerResult result)
        {
            foreach (Heroes.ReplayParser.MPQFiles.GameEvent ping in result.Pings)
            {
                yield return new StormPlayer(ping.player, result.Start, ping.TimeSpan, GameEvent.Ping);
            }
        }

        private IEnumerable<StormPlayer> HandleMapObjectives(AnalyzerResult result)
        {
            foreach (Unit unit in result.MapObjectives)
            {
                if (unit.PlayerKilledBy != null)
                {
                    yield return new StormPlayer(unit.PlayerKilledBy, result.Start, unit.TimeSpanDied.Value, GameEvent.MapObjective);
                }
                else if (unit.TimeSpanDied.HasValue && unit.PlayerKilledBy == null)
                {
                    Player player = (from alive in result.Alive
                                     from heroUnit in alive.HeroUnits
                                     from position in heroUnit.Positions
                                     where heroUnit.IsAlive(result.Start, result.End)
                                     where position.TimeSpan.IsWithin(result.Start, unit.TimeSpanDied.Value)
                                     let distance = position.Point.DistanceTo(unit.PointDied)
                                     orderby distance
                                     select alive).FirstOrDefault();

                    if (player != null)
                    {
                        logger.LogDebug($"[{GameEvent.MapObjective}][{unit.Name}][{player.HeroId}]");
                        yield return new StormPlayer(player, result.Start, unit.TimeSpanDied.Value, GameEvent.MapObjective);
                    }
                }
                else if (unit.IsCapturePoint())
                {
                    OwnerChangeEvent? changeEvent = unit.OwnerChangeEvents.FirstOrDefault(e => e.TimeSpanOwnerChanged.IsWithin(result.Start, result.End));
                    
                    if (changeEvent != null)
                    {
                        foreach (Unit heroUnit in result.Alive.SelectMany(player => player.HeroUnits.Where(heroUnit => heroUnit.IsAlive(result.Start, result.End) && heroUnit.Team == changeEvent.Team)))
                        {
                            if (heroUnit.Positions.Any(position => position.TimeSpan.IsWithin(result.Start, result.End) && position.Point.DistanceTo(unit.PointBorn) <= MAX_DISTANCE_OWNER_CHANGE))
                            {
                                logger.LogDebug($"[{GameEvent.MapObjective}][{unit.Name}][OwnerChangeEvent][{heroUnit.PlayerControlledBy.HeroId}]");

                                yield return new StormPlayer(heroUnit.PlayerControlledBy, result.Start, changeEvent.TimeSpanOwnerChanged, GameEvent.MapObjective);
                            }
                        }
                    }
                }
            }
        }

        private IEnumerable<StormPlayer> HandleStructures(AnalyzerResult result)
        {
            foreach (Unit unit in result.Structures)
            {
                yield return new StormPlayer(unit.PlayerKilledBy, result.Start, unit.TimeSpanDied.Value, GameEvent.Structure);
            }
        }

        private IEnumerable<StormPlayer> HandleAlive(AnalyzerResult result)
        {
            if (result.Alive.Except(result.NearSpawn).Any())
            {
                foreach (Player player in result.Alive.Except(result.NearSpawn))
                {
                    yield return new StormPlayer(player, result.Start, result.Duration, GameEvent.Alive);
                }
            }
            else
            {
                foreach (Player player in result.Alive)
                {
                    yield return new StormPlayer(player, result.Start, result.Duration, GameEvent.Alive);
                }
            }
        }

        private IEnumerable<StormPlayer> HandleTeamObjectives(AnalyzerResult result)
        {
            foreach (TeamObjective objective in result.TeamObjectives)
            {
                yield return new StormPlayer(objective.Player, result.Start, objective.TimeSpan, GameEvent.TeamObjective);
            }
        }

        private IEnumerable<StormPlayer> HandleCampCaptures(AnalyzerResult result)
        {
            foreach ((Player Player, TimeSpan Time) item in result.CampCaptures)
            {
                yield return new StormPlayer(item.Player, result.Start, item.Time, GameEvent.MercenaryCamp);
            }
        }

        private IEnumerable<StormPlayer> HandleKills(AnalyzerResult result, GameEvent @event)
        {
            IEnumerable<IGrouping<Player, Unit>> playerKills = result.Deaths.GroupBy(unit => unit.PlayerKilledBy).Where(kills => kills.Count() == @event.ToKills());

            foreach (IGrouping<Player, Unit> players in playerKills)
            {
                Player killer = players.Key;
                TimeSpan maxTime = players.Max(unit => unit.TimeSpanDied.Value);
                Hero? hero = killer.TryGetHero();

                if (hero != null)
                {
                    if (hero.IsMelee)
                    {
                        if (hero == Constants.Heroes.Abathur)
                        {
                            foreach (Unit death in players)
                            {
                                yield return new StormPlayer(death.PlayerControlledBy, result.Start, death.TimeSpanDied.Value.Add(TimeSpan.FromSeconds(2)), GameEvent.Death);
                            }
                        }
                        else
                        {
                            yield return new StormPlayer(killer, result.Start, maxTime.Add(TimeSpan.FromSeconds(2)), @event);
                        }
                    }
                    else if (hero.IsRanged)
                    {
                        foreach (Unit death in players)
                        {
                            yield return new StormPlayer(death.PlayerControlledBy, result.Start, death.TimeSpanDied.Value.Add(TimeSpan.FromSeconds(1)), GameEvent.Death);
                        }
                    }
                }
                else
                {
                    yield return new StormPlayer(killer, result.Start, maxTime.Add(TimeSpan.FromSeconds(2)), @event);
                }
            }
        }
    }
}
