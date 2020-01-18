using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;

namespace HeroesReplay.Spectator
{
    public class StormPlayerSelector
    {
        public List<StormPlayer> Select(AnalyzerResult result, params SelectorCriteria[] selectorCriterion)
        {
            List<StormPlayer> players = new List<StormPlayer>();

            foreach (var selectorCriteria in selectorCriterion)
            {
                players.AddRange(selectorCriteria switch
                {
                    SelectorCriteria.Alive => HandleAlive(result),
                    SelectorCriteria.Death => HandleDeaths(result),
                    SelectorCriteria.MapObjective => HandleMapObjectives(result),
                    SelectorCriteria.Ping => HandlePings(result),
                    SelectorCriteria.Kill => HandleKills(result, 1),
                    SelectorCriteria.MultiKill => HandleKills(result, 2),
                    SelectorCriteria.TripleKill => HandleKills(result, 3),
                    SelectorCriteria.QuadKill => HandleKills(result, 4),
                    SelectorCriteria.PentaKill => HandleKills(result, 5),
                    SelectorCriteria.TeamObjective => HandleTeamObjectives(result),
                    SelectorCriteria.Structure => HandleStructures(result),
                    SelectorCriteria.Any => HandleAny(result),
                });
            }

            return players.OrderBy(x => x.When).ToList();
        }

        private IEnumerable<StormPlayer> HandleKills(AnalyzerResult result, int count)
        {
            IOrderedEnumerable<IGrouping<Player, Unit>> playerKills = result.PlayerDeaths.GroupBy(unit => unit.PlayerKilledBy).OrderBy(kills => kills.Count());

            foreach (IGrouping<Player, Unit> players in playerKills)
            {
                Player player = players.Key;
                int kills = players.Count();
                TimeSpan maxTime = players.Max(unit => unit.TimeSpanDied.Value);
                Hero? hero = player.TryGetHero();

                if (kills == count)
                {
                    yield return new StormPlayer(player, maxTime, count switch { 1 => SelectorCriteria.Kill, 2 => SelectorCriteria.MultiKill, 3 => SelectorCriteria.TripleKill, 4 => SelectorCriteria.QuadKill, 5 => SelectorCriteria.PentaKill });
                }
            }
        }

        private IEnumerable<StormPlayer> HandleAny(AnalyzerResult result)
        {
            return result.PlayerDeaths.Any() ? HandleDeaths(result) :
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
                yield return new StormPlayer(ping.player, ping.TimeSpan, SelectorCriteria.Ping);
            }
        }

        private IEnumerable<StormPlayer> HandleMapObjectives(AnalyzerResult result)
        {
            foreach (Unit unit in result.MapObjectives)
            {
                yield return new StormPlayer(unit.PlayerKilledBy ?? unit.PlayerControlledBy, unit.TimeSpanDied.Value, SelectorCriteria.MapObjective);
            }
        }

        private IEnumerable<StormPlayer> HandleDeaths(AnalyzerResult result)
        {
            foreach (Unit unit in result.PlayerDeaths.OrderByDeath())
            {
                yield return new StormPlayer(unit.PlayerControlledBy, unit.TimeSpanDied.Value, SelectorCriteria.Death);
            }
        }

        private IEnumerable<StormPlayer> HandleStructures(AnalyzerResult result)
        {
            foreach (Unit unit in result.Structures)
            {
                yield return new StormPlayer(unit.PlayerKilledBy, unit.TimeSpanDied.Value, SelectorCriteria.Structure);
            }
        }

        private IEnumerable<StormPlayer> HandleAlive(AnalyzerResult result)
        {
            IEnumerable<IGrouping<int, Player>> teams = result.PlayersAlive.GroupBy(p => p.Team).ToList();
            IEnumerable<Player> equalDistribution = teams.First().Interleave(teams.Last()).ToList();
            
            foreach (Player player in equalDistribution.Take(2))
            {
                yield return new StormPlayer(player, result.Duration / 2, SelectorCriteria.Alive);
            }
        }

        private IEnumerable<StormPlayer> HandleTeamObjectives(AnalyzerResult result)
        {
            foreach (TeamObjective objective in result.TeamObjectives)
            {
                yield return new StormPlayer(objective.Player, objective.TimeSpan, SelectorCriteria.TeamObjective);
            }

            /*
             * Standard camps are not captured in TeamObjectives
             * https://github.com/barrett777/Heroes.ReplayParser/blob/2d29bf2f66bfd44c471a4214698e6b517d38ecd3/Heroes.ReplayParser/Statistics.cs#L343
             */
            foreach (var (player, capture) in result.Camps)
            {
                yield return new StormPlayer(player, capture, SelectorCriteria.TeamObjective);
            }
        }
    }
}
