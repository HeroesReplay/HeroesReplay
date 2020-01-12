using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;

namespace HeroesReplay
{
    public static class HeroesReplayParserExtensions
    {
        public static bool IsPlayerReferenced(this Unit unit) => unit?.PlayerKilledBy != null || unit?.PlayerControlledBy != null;
        public static bool IsHero(this Unit unit) => unit?.Group == Unit.UnitGroup.Hero;
        public static bool IsMapObjective(this Unit unit) => unit?.Group == Unit.UnitGroup.MapObjective;
        public static bool IsCamp(this Unit unit) => unit?.Group == Unit.UnitGroup.MercenaryCamp;
        public static bool IsStructure(this Unit unit) => unit?.Group == Unit.UnitGroup.Structures;
        public static bool IsMinions(this Unit unit) => unit?.Group == Unit.UnitGroup.Minions;
        public static bool IsMiscellaneous(this Unit unit) => unit?.Group == Unit.UnitGroup.Miscellaneous;
        public static bool IsWithin(this TimeSpan value, TimeSpan start, TimeSpan end) => value > start && value < end;
        public static bool DiesWithin(this Unit unit, TimeSpan start, TimeSpan end) => unit?.TimeSpanDied != null && unit.TimeSpanDied.Value.IsWithin(start, end);
        public static IOrderedEnumerable<Unit> OrderByDeath(this IEnumerable<Unit> units) => units.OrderBy(unit => unit?.TimeSpanDied.Value);
    }
}
