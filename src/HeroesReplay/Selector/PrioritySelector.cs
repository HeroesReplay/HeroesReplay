using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;

namespace HeroesReplay.Selector
{
    public class PrioritySelector
    {
        readonly List<PlayerSelect> selections = new List<PlayerSelect>();

        public List<PlayerSelect> Prioritize(AnalyzerResult result)
        {
            selections.Clear();

            if (result.Deaths.Any()) HandleDeaths(result);
            else if (result.MapObjectives.Any()) HandleMapObjectives(result);
            else if (result.TeamObjectives.Any()) HandleTeamObjectives(result);
            else if (result.Structures.Any()) HandleStructures(result);
            else if (result.Alive.Any()) HandleAlive(result);
            return selections;
        }

        private void HandleMapObjectives(AnalyzerResult result)
        {
            foreach (var unit in result.MapObjectives)
            {
                selections.Add(new PlayerSelect(unit.PlayerKilledBy ?? unit.PlayerControlledBy, unit.TimeSpanDied.Value - result.Start, SelectorReason.MapObjective));
            }
        }

        private void HandleDeaths(AnalyzerResult result)
        {
            // IOrderedEnumerable<IGrouping<Player, Unit>> orderedEnumerable = result.Deaths.GroupBy(unit => unit.PlayerKilledBy).OrderBy(kills => kills.Count());
            // TODO: selection based on kill count

            foreach (Unit unit in result.Deaths)
            {
                selections.Add(new PlayerSelect(unit.PlayerControlledBy, unit.TimeSpanDied.Value - result.Start, SelectorReason.Death));
            }
        }

        private void HandleStructures(AnalyzerResult result)
        {
            foreach (Unit unit in result.Structures.Take(2))
            {
                selections.Add(new PlayerSelect(unit.PlayerKilledBy, unit.TimeSpanDied.Value - result.Start, SelectorReason.Structure));
            }
        }

        private void HandleAlive(AnalyzerResult result)
        {
            foreach (Player player in result.Alive.Take(2))
            {
                selections.Add(new PlayerSelect(player, TimeSpan.FromSeconds(5), SelectorReason.Alive));
            }
        }

        private void HandleTeamObjectives(AnalyzerResult result)
        {
            foreach (TeamObjective objective in result.TeamObjectives)
            {
                selections.Add(new PlayerSelect(objective.Player, objective.TimeSpan - result.Start, SelectorReason.TeamObjective));
            }
        }
    }
}
