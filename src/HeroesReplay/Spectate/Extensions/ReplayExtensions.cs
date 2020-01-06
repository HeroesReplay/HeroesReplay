using Heroes.ReplayParser;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay
{
    public static class ReplayExtensions
    {
        public static Player PickRandomPlayer(this IEnumerable<Player> players)
        {
            return players.OrderBy(p => Guid.NewGuid()).First();
            // return players.OrderBy(p => p.HeroUnits.Count).ThenByDescending(p => p.CharacterLevel).First();
        }

        public static Player PickFocusPlayer(this IEnumerable<Unit> units)
        {
            // if we can sort and pick player with most kills, lets do that
            if (units.Any(u => u.PlayerKilledBy != null))
            {
                return units.Where(u => u.PlayerKilledBy != null).GroupBy(unit => unit.PlayerKilledBy, unit => unit).OrderByDescending(kills => kills.Count()).First().Key;
            }

            // otherwise just pick a random player
            return units.Select(unit => unit.PlayerControlledBy).OrderBy(u => Guid.NewGuid()).Distinct().First();
        }

        public static bool WithinViewSpan(this Unit unit, TimeSpan timer, TimeSpan upper) => unit.TimeSpanDied.HasValue && unit.TimeSpanDied.Value > timer && unit.TimeSpanDied.Value.Subtract(timer) <= upper;

        public static bool WillDie(this Unit unit) => unit.TimeSpanDied.HasValue;

        public static bool WithinViewSpan(this TimeSpan value, TimeSpan timer, TimeSpan upper) => value > timer && value.Subtract(timer) <= upper;

        public static bool WithinViewSpan(this TeamObjective objective, TimeSpan timer, TimeSpan upper) => objective.TimeSpan > timer && objective.TimeSpan.Subtract(timer) <= upper;
    }
}
