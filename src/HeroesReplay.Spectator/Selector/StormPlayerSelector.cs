using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;

namespace HeroesReplay.Spectator
{
    public class StormPlayerSelector
    {
        public IEnumerable<StormPlayer> Select(AnalyzerResult result)
        {
            if (result.PlayerDeaths.Any())
            {
                return HandleDeaths(result);
            }
            else if (result.MapObjectives.Any())
            {
                return HandleMapObjectives(result);
            }
            else if (result.TeamObjectives.Any())
            {
                return HandleTeamObjectives(result);
            }
            else if (result.Structures.Any())
            {
                return HandleStructures(result);
            }
            else if (result.PingSources.Any())
            {
                return HandlePings(result);
            }
            else if (result.PlayersAlive.Any())
            {
                return HandleAlive(result);
            }

            return Enumerable.Empty<StormPlayer>();
        }

        private IEnumerable<StormPlayer> HandlePings(AnalyzerResult result)
        {
            foreach (GameEvent ping in result.PingSources)
            {
                yield return new StormPlayer(ping.player, ping.TimeSpan, SelectorReason.Ping);
            }
        }

        private IEnumerable<StormPlayer> HandleMapObjectives(AnalyzerResult result)
        {
            foreach (var unit in result.MapObjectives)
            {
                yield return new StormPlayer(unit.PlayerKilledBy ?? unit.PlayerControlledBy, unit.TimeSpanDied.Value, SelectorReason.MapObjective);
            }
        }

        private IEnumerable<StormPlayer> HandleDeaths(AnalyzerResult result)
        {
            // IOrderedEnumerable<IGrouping<StormPlayer, Unit>> orderedEnumerable = result.PlayerDeaths.GroupBy(unit => unit.PlayerKilledBy).OrderBy(kills => kills.Count());
            // TODO: selection based on kill count

            foreach (Unit unit in result.PlayerDeaths)
            {
                yield return new StormPlayer(unit.PlayerControlledBy, unit.TimeSpanDied.Value, SelectorReason.Death);
            }
        }

        private IEnumerable<StormPlayer> HandleStructures(AnalyzerResult result)
        {
            foreach (Unit unit in result.Structures)
            {
                yield return new StormPlayer(unit.PlayerKilledBy, unit.TimeSpanDied.Value, SelectorReason.Structure);
            }
        }

        private IEnumerable<StormPlayer> HandleAlive(AnalyzerResult result)
        {
            IEnumerable<IGrouping<int, Player>> teams = result.PlayersAlive.GroupBy(p => p.Team);
            IEnumerable<Player> equalDistribution = teams.First().Interleave(teams.Last());

            foreach (Player player in equalDistribution)
            {
                yield return new StormPlayer(player, result.Duration / result.PlayersAlive.Count, SelectorReason.Alive);
            }
        }

        private IEnumerable<StormPlayer> HandleTeamObjectives(AnalyzerResult result)
        {
            foreach (TeamObjective objective in result.TeamObjectives)
            {
                yield return new StormPlayer(objective.Player, objective.TimeSpan, SelectorReason.TeamObjective);
            }
        }
    }
}
