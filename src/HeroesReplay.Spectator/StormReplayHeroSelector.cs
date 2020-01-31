using HeroesReplay.Analyzer;
using HeroesReplay.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;

namespace HeroesReplay.Spectator
{
    public class StormReplayHeroSelector
    {
        private readonly ILogger<StormReplayHeroSelector> logger;

        private const int MAX_DISTANCE_TO_WATCH_TOWER = 40;

        public StormReplayHeroSelector(ILogger<StormReplayHeroSelector> logger)
        {
            this.logger = logger;
        }

        public IEnumerable<StormPlayer> Select(AnalyzerResult result, GameEvent gameEvent) => (gameEvent switch
        {
            GameEvent.DangerZone => HandleDangerZone(result),
            GameEvent.Alive => HandleAlive(result),
            GameEvent.KilledEnemy => HandlePreviousKillers(result),
            GameEvent.Ping => HandlePings(result),
            GameEvent.Structure => HandleStructures(result),
            GameEvent.MapObjective => HandleMapObjectives(result),
            GameEvent.TeamObjective => HandleTeamObjectives(result),
            GameEvent.MercenaryCamp => HandleCampCaptures(result),
            GameEvent.MercenaryKill => HandleCampKills(result),
            GameEvent.Death => HandleDeaths(result),
            GameEvent.Kill => HandleKills(result, GameEvent.Kill),
            GameEvent.MultiKill => HandleKills(result, GameEvent.MultiKill),
            GameEvent.TripleKill => HandleKills(result, GameEvent.TripleKill),
            GameEvent.QuadKill => HandleKills(result, GameEvent.QuadKill),
            GameEvent.PentaKill => HandleKills(result, GameEvent.PentaKill),
            _ => throw new ArgumentOutOfRangeException(nameof(gameEvent), gameEvent, null)
        })
        .OrderBy(player => player.When);

        private IEnumerable<StormPlayer> HandleDangerZone(AnalyzerResult result)
        {
            foreach (Player player in result.DangerZone)
            {
                yield return new StormPlayer(player, result.Start, result.Duration, GameEvent.DangerZone);
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
                yield return new StormPlayer(death.PlayerControlledBy, result.Start, death.TimeSpanDied.Value, GameEvent.Death);
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
                    Unit playerUnit =
                        (from player in result.Alive
                         from heroUnit in player.HeroUnits
                         from position in heroUnit.Positions
                         where heroUnit.TimeSpanBorn <= result.Start && heroUnit.TimeSpanDied > result.End
                         where position.TimeSpan.IsWithin(result.Start, unit.TimeSpanDied.Value)
                         let distance = position.Point.DistanceTo(unit.PointDied)
                         orderby distance
                         select heroUnit).FirstOrDefault();

                    if (playerUnit != null)
                    {
                        logger.LogDebug($"[{GameEvent.MapObjective}][{unit.Name}][{playerUnit.PlayerControlledBy.HeroId}]");
                        yield return new StormPlayer(playerUnit.PlayerControlledBy, result.Start, unit.TimeSpanDied.Value, GameEvent.MapObjective);
                    }
                }
                else if (unit.TimeSpanBorn == TimeSpan.Zero && unit.TimeSpanDied == null)
                {
                    OwnerChangeEvent? changeEvent = unit.OwnerChangeEvents.FirstOrDefault(e => e.TimeSpanOwnerChanged.IsWithin(result.Start, result.End));

                    if (changeEvent != null)
                    {
                        foreach (Unit heroUnit in result.Alive.SelectMany(player => player.HeroUnits.Where(heroUnit => heroUnit.TimeSpanBorn <= result.Start && unit.TimeSpanDied >= result.End && heroUnit.Team == changeEvent.Team)))
                        {
                            if (heroUnit.Positions.Any(p => p.TimeSpan.IsWithin(result.Start, result.End) && p.Point.DistanceTo(unit.PointBorn) < MAX_DISTANCE_TO_WATCH_TOWER))
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
            foreach (TeamObjective objective in result.TeamObjectives.ToList())
            {
                yield return new StormPlayer(objective.Player, result.Start, objective.TimeSpan, GameEvent.TeamObjective);
            }
        }

        private IEnumerable<StormPlayer> HandleCampCaptures(AnalyzerResult result)
        {
            foreach ((Player Player, TimeSpan Time) item in result.CampCaptures.ToList())
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
                                yield return new StormPlayer(death.PlayerControlledBy, result.Start, death.TimeSpanDied.Value, @event);
                            }
                        }
                        else
                        {
                            yield return new StormPlayer(killer, result.Start, maxTime, @event);
                        }
                    }
                    else if (hero.IsRanged)
                    {
                        foreach (Unit death in players)
                        {
                            yield return new StormPlayer(death.PlayerControlledBy, result.Start, death.TimeSpanDied.Value, @event);
                        }
                    }
                }
                else
                {
                    yield return new StormPlayer(killer, result.Start, maxTime, @event);
                }
            }
        }
    }
}
