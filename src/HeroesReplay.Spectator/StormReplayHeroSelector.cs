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

        public StormReplayHeroSelector(ILogger<StormReplayHeroSelector> logger)
        {
            this.logger = logger;
        }

        public IEnumerable<StormPlayer> Select(AnalyzerResult result, GameCriteria gameCriteria) => (gameCriteria switch
        {
            GameCriteria.Alive => HandleAlive(result),
            GameCriteria.PreviousAliveKiller => HandlePreviousKillers(result),
            GameCriteria.Ping => HandlePings(result),
            GameCriteria.Structure => HandleStructures(result),
            GameCriteria.MapObjective => HandleMapObjectives(result),
            GameCriteria.TeamObjective => HandleTeamObjectives(result),
            GameCriteria.CampCapture => HandleCampCaptures(result),
            GameCriteria.MercenaryKill => HandleCampKills(result),
            GameCriteria.Death => HandleDeaths(result),
            GameCriteria.Kill => HandleKills(result, GameCriteria.Kill),
            GameCriteria.MultiKill => HandleKills(result, GameCriteria.MultiKill),
            GameCriteria.TripleKill => HandleKills(result, GameCriteria.TripleKill),
            GameCriteria.QuadKill => HandleKills(result, GameCriteria.QuadKill),
            GameCriteria.PentaKill => HandleKills(result, GameCriteria.PentaKill)
        })
        .OrderBy(player => player.When);

        private IEnumerable<StormPlayer> HandleCampKills(AnalyzerResult result) => result.Mercenaries.Select(unit => new StormPlayer(unit.Player, result.Start, unit.TimeDied, GameCriteria.MercenaryKill));

        private IEnumerable<StormPlayer> HandlePreviousKillers(AnalyzerResult result) => result.Killers.Select(killer => new StormPlayer(killer, result.Start, result.Duration, GameCriteria.PreviousAliveKiller));

        private IEnumerable<StormPlayer> HandleDeaths(AnalyzerResult result) => result.Deaths.Select(death => new StormPlayer(death.PlayerControlledBy, result.Start, death.TimeSpanDied.Value, GameCriteria.Death));

        private IEnumerable<StormPlayer> HandlePings(AnalyzerResult result) => result.Pings.Select(ping => new StormPlayer(ping.player, result.Start, ping.TimeSpan, GameCriteria.Ping));

        private IEnumerable<StormPlayer> HandleMapObjectives(AnalyzerResult result)
        {
            foreach (Unit unit in result.MapObjectives)
            {
                if (unit.PlayerKilledBy != null)
                {
                    yield return new StormPlayer(unit.PlayerKilledBy, result.Start, unit.TimeSpanDied.Value, GameCriteria.MapObjective);
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
                        yield return new StormPlayer(playerUnit.PlayerControlledBy, result.Start, unit.TimeSpanDied.Value, GameCriteria.MapObjective);

                }
                else if (unit.TimeSpanBorn == TimeSpan.Zero && unit.TimeSpanDied == null)
                {
                    OwnerChangeEvent? changeEvent = unit.OwnerChangeEvents.FirstOrDefault(e => e.TimeSpanOwnerChanged.IsWithin(result.Start, result.End));

                    if (changeEvent != null)
                    {
                        foreach (Unit heroUnit in result.Alive.SelectMany(player => player.HeroUnits.Where(heroUnit => heroUnit.TimeSpanBorn <= result.Start && unit.TimeSpanDied > result.End && heroUnit.Team == changeEvent.Team)))
                        {
                            if (heroUnit.Positions.Any(p => p.TimeSpan.IsWithin(result.Start, result.End) && p.Point.DistanceTo(unit.PointBorn) < 10))
                            {
                                yield return new StormPlayer(heroUnit.PlayerControlledBy, result.Start, changeEvent.TimeSpanOwnerChanged, GameCriteria.MapObjective);
                            }
                        }
                    }
                }
            }
        }

        private IEnumerable<StormPlayer> HandleStructures(AnalyzerResult result) => result.Structures.Select(unit => new StormPlayer(unit.PlayerKilledBy, result.Start, unit.TimeSpanDied.Value, GameCriteria.Structure));

        private IEnumerable<StormPlayer> HandleAlive(AnalyzerResult result) => result.Alive.Select(player => new StormPlayer(player, result.Start, result.Duration, GameCriteria.Alive));

        private IEnumerable<StormPlayer> HandleTeamObjectives(AnalyzerResult result) => result.TeamObjectives.Select(objective => new StormPlayer(objective.Player, result.Start, objective.TimeSpan, GameCriteria.TeamObjective));

        /// <summary>
        /// Standard camps are not captured in TeamObjectives
        /// </summary>
        /// <remarks>
        /// https://github.com/barrett777/Heroes.ReplayParser/blob/2d29bf2f66bfd44c471a4214698e6b517d38ecd3/Heroes.ReplayParser/Statistics.cs#L343
        /// </remarks>
        private IEnumerable<StormPlayer> HandleCampCaptures(AnalyzerResult result)
        {
            foreach ((Player player, TimeSpan capture) in result.CampCaptures)
            {
                yield return new StormPlayer(player, result.Start, capture, GameCriteria.CampCapture);
            }
        }

        private IEnumerable<StormPlayer> HandleKills(AnalyzerResult result, GameCriteria criteria)
        {
            IEnumerable<IGrouping<Player, Unit>> playerKills = result.Deaths.GroupBy(unit => unit.PlayerKilledBy).Where(kills => kills.Count() == criteria.ToKills());

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
                                yield return new StormPlayer(death.PlayerControlledBy, result.Start, death.TimeSpanDied.Value, criteria);
                            }
                        }
                        else
                        {
                            yield return new StormPlayer(killer, result.Start, maxTime, criteria);
                        }
                    }
                    else if (hero.IsRanged)
                    {
                        foreach (Unit death in players)
                        {
                            yield return new StormPlayer(death.PlayerControlledBy, result.Start, death.TimeSpanDied.Value, criteria);
                        }
                    }
                }
                else
                {
                    yield return new StormPlayer(killer, result.Start, maxTime, criteria);
                }
            }
        }
    }
}
