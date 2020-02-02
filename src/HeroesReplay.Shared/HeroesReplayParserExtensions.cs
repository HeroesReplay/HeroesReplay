using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;

namespace HeroesReplay.Shared
{
    public static class HeroesReplayParserExtensions
    {
        public static bool IsPlayerReferenced(this Unit unit) => unit?.PlayerKilledBy != null || unit?.PlayerControlledBy != null;
        public static bool IsMercCapture(this Unit unit) => unit.Name.Equals("VolskayaMercCaptureSlab") || unit.Name.Equals("DragonballCaptureBeacon") || unit.Name.Equals("TownMercCampCaptureBeacon");
        public static bool IsWatchTowerCapture(this Unit unit) => unit.Name.Contains("WatchTower");
        public static bool IsMinion(this Unit unit) => unit.Name.EndsWith("Minion");
        public static bool IsHero(this Unit unit) => unit.Name.StartsWith("Hero") || unit.Name.Equals("LongboatRaidBoat") || unit.Name.Equals("MurkyRespawnEgg");
        public static bool IsMapObjective(this Unit unit) => unit.Group == Unit.UnitGroup.MapObjective ||
                                                             unit.Name.StartsWith("BossDuel") ||
                                                             unit.Name.Contains("Vehicle") ||
                                                             unit.Name.Equals("Shambler") ||
                                                             unit.Name.Equals("Seed") ||
                                                             unit.Name.Equals("Payload_Neutral") ||
                                                             unit.Name.Equals("GardenTerror") ||
                                                             unit.Name.Equals("HordeCavalry") ||
                                                             unit.Name.EndsWith("CaptureCage") ||
                                                             unit.IsCapturePoint();
        public static bool IsCapturePoint(this Unit unit) => unit.TimeSpanBorn == TimeSpan.Zero &&
                                                             unit.TimeSpanDied == null &&
                                                             unit.OwnerChangeEvents.Any()
                                                             && !unit.Name.Contains("IconUnit") && !unit.Name.Equals("LootBannerSconce") && !unit.Name.Contains("Minimap");

        public static bool IsCamp(this Unit unit) => unit.Group == Unit.UnitGroup.MercenaryCamp ||
                                                     unit.Name.StartsWith("Merc") ||
                                                     unit.Name.Equals("OverwatchTurret") ||
                                                     unit.Name.Equals("TerranGoliath") ||
                                                     unit.Name.StartsWith("JungleGrave");

        public static bool IsStructure(this Unit unit) => unit.Team.HasValue && unit.Name.StartsWith("Town") || unit.IsCore();

        public static bool IsCore(this Unit unit) => unit.Name.Equals("KingsCore") || unit.Name.Equals("VanndarStormpike") || unit.Name.Equals("DrekThar");

        public static bool IsWithin(this TimeSpan value, TimeSpan start, TimeSpan end) => value >= start && value <= end;
        public static bool IsDead(this Unit unit, TimeSpan start, TimeSpan end) => unit.TimeSpanDied.HasValue && unit.TimeSpanDied.Value.IsWithin(start, end);
        public static bool IsAlive(this Unit unit, TimeSpan start, TimeSpan end) => unit.TimeSpanBorn <= start && (unit.TimeSpanDied == null || unit.TimeSpanDied.Value > end);
        public static Point GetSpawn(this Unit unit) => unit.PlayerControlledBy.HeroUnits[0].PointBorn;
        public static Hero? TryGetHero(this Player player) => Constants.Heroes.All.Find(hero => hero.Name.Equals(player.HeroId, StringComparison.InvariantCultureIgnoreCase));
        public static List<MatchAwardType> GetMatchAwards(this Replay replay) => replay.Players.SelectMany(p => p.ScoreResult.MatchAwards).Distinct().ToList();
        public static IEnumerable<string> GetText(this MatchAwardType matchAwardType) => Constants.MatchAwards[matchAwardType];
        public static IEnumerable<string> ToText(this IEnumerable<MatchAwardType> matchAwardTypes) => matchAwardTypes.SelectMany(mat => mat.GetText()).Distinct();
    }
}
