using HeroesReplay.Analyzer;
using HeroesReplay.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Spectator
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Heroes.ReplayParser;

    public class StormReplayHeroSelector
    {
        private readonly ILogger<StormReplayHeroSelector> logger;

        public StormReplayHeroSelector(ILogger<StormReplayHeroSelector> logger)
        {
            this.logger = logger;
        }

        public List<StormPlayer> Select(AnalyzerResult result, GameCriteria gameCriteria)
        {
            List<StormPlayer> players = new List<StormPlayer>();

            players.AddRange(gameCriteria switch
            {
                GameCriteria.Alive => HandleAlive(result),
                GameCriteria.Ping => HandlePings(result),
                GameCriteria.Structure => HandleStructures(result),
                GameCriteria.MapObjective => HandleMapObjectives(result),
                GameCriteria.TeamObjective => HandleTeamObjectives(result),
                GameCriteria.CampObjective => HandleCampObjectives(result),
                // GameCriteria.Death => HandleDeaths(result),
                GameCriteria.Kill => HandleKills(result, 1),
                GameCriteria.MultiKill => HandleKills(result, 2),
                GameCriteria.TripleKill => HandleKills(result, 3),
                GameCriteria.QuadKill => HandleKills(result, 4),
                GameCriteria.PentaKill => HandleKills(result, 5),
                GameCriteria.Any => HandleAny(result),
            });

            players.Sort((playerA, playerB) => playerA.When.CompareTo(playerB.When));

            return players;
        }

        private IEnumerable<StormPlayer> HandleKills(AnalyzerResult result, int killCount)
        {
            IEnumerable<IGrouping<Player, Unit>> playerKills = result.PlayerDeaths.GroupBy(unit => unit.PlayerKilledBy).Where(kills => kills.Count() == killCount);

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
                            foreach (var death in players)
                            {
                                yield return new StormPlayer(death.PlayerControlledBy, result.Start, maxTime, killCount.ToCriteria());
                            }
                        }
                        else
                        {
                            yield return new StormPlayer(killer, result.Start, maxTime, killCount.ToCriteria());
                        }
                    }
                    else if (hero.IsRanged)
                    {
                        yield return new StormPlayer(killer, result.Start, maxTime, killCount.ToCriteria());
                    }
                }
                else
                {
                    yield return new StormPlayer(killer, result.Start, maxTime, killCount.ToCriteria());
                }
            }
        }

        private IEnumerable<StormPlayer> HandleAny(AnalyzerResult result)
        {
            return result.PlayerDeaths.Any() ? HandleKills(result, 1) :
                result.MapObjectives.Any() ? HandleMapObjectives(result) :
                result.TeamObjectives.Any() ? HandleTeamObjectives(result) :
                result.Structures.Any() ? HandleStructures(result) :
                result.PingSources.Any() ? HandlePings(result) :
                result.PlayersAlive.Any() ? HandleAlive(result) : Enumerable.Empty<StormPlayer>();
        }

        // Ping events are only from the team which the replay file originates from
        private IEnumerable<StormPlayer> HandlePings(AnalyzerResult result)
        {
            foreach (GameEvent ping in result.PingSources)
            {
                yield return new StormPlayer(ping.player, result.Start, ping.TimeSpan, GameCriteria.Ping);
            }
        }

        private IEnumerable<StormPlayer> HandleMapObjectives(AnalyzerResult result)
        {
            foreach (Unit unit in result.MapObjectives)
            {
                yield return new StormPlayer(unit.PlayerKilledBy ?? unit.PlayerControlledBy, result.Start, unit.TimeSpanDied.Value, GameCriteria.MapObjective);
            }
        }

        private IEnumerable<StormPlayer> HandleStructures(AnalyzerResult result)
        {
            foreach (Unit unit in result.Structures)
            {
                yield return new StormPlayer(unit.PlayerKilledBy, result.Start, unit.TimeSpanDied.Value, GameCriteria.Structure);
            }
        }

        private IEnumerable<StormPlayer> HandleAlive(AnalyzerResult result)
        {
            foreach (Player player in result.PlayersAlive)
            {
                yield return new StormPlayer(player, result.Start, result.Duration, GameCriteria.Alive);
            }
        }

        private IEnumerable<StormPlayer> HandleTeamObjectives(AnalyzerResult result)
        {
            foreach (TeamObjective objective in result.TeamObjectives)
            {
                yield return new StormPlayer(objective.Player, result.Start, objective.TimeSpan, GameCriteria.TeamObjective);
            }
        }

        /// <summary>
        /// Standard camps are not captured in TeamObjectives
        /// </summary>
        /// <remarks>
        /// https://github.com/barrett777/Heroes.ReplayParser/blob/2d29bf2f66bfd44c471a4214698e6b517d38ecd3/Heroes.ReplayParser/Statistics.cs#L343
        /// </remarks>
        private IEnumerable<StormPlayer> HandleCampObjectives(AnalyzerResult result)
        {
            foreach ((Player player, TimeSpan capture) in result.Camps)
            {
                yield return new StormPlayer(player, result.Start, capture, GameCriteria.CampObjective);
            }
        }
    }
}
