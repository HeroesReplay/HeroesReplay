using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;

namespace HeroesReplay.Core.Shared
{
    public static class HeroesReplayParserExtensions
    {
        public static int GetAbilityLink(this TrackerEventStructure structure)
        {            
            return Convert.ToInt32(structure?.array[1]?.array[0]?.unsignedInt ?? 0); // m_abilLink
        }

        public static int GetAbilityCmdIndex(this TrackerEventStructure trackerEvent)
        {
            return Convert.ToInt32(trackerEvent.array[1]?.array[1]?.unsignedInt ?? 0); // m_abilCmdIndex
        }

        public static bool IsTaunt(this Replay replay, GameEvent gameEvent)
        {
            return gameEvent.eventType == GameEventType.CCmdEvent && 

                   (gameEvent.data.GetAbilityLink() == 19 && replay.ReplayBuild < 68740 || gameEvent.data.GetAbilityLink() == 22 && replay.ReplayBuild >= 68740) && 
                   gameEvent.data.GetAbilityCmdIndex() == 4;
        }

        public static bool IsDance(this Replay replay, GameEvent gameEvent)
        {
            return gameEvent.eventType == GameEventType.CCmdEvent && 

                   (gameEvent.data.GetAbilityLink() == 19 && replay.ReplayBuild < 68740 || gameEvent.data.GetAbilityLink() == 22 && replay.ReplayBuild >= 68740) && 
                   gameEvent.data.GetAbilityCmdIndex() == 3;
        }

        public static bool IsHearthStone(this Replay replay, GameEvent gameEvent)
        {
            return

                gameEvent.eventType == GameEventType.CCmdEvent &&

                (replay.ReplayBuild < 61872 && gameEvent.data.GetAbilityLink() == 200 ||
                 replay.ReplayBuild >= 61872 && replay.ReplayBuild < 68740 && gameEvent.data.GetAbilityLink() == 119 ||
                 replay.ReplayBuild >= 68740 && replay.ReplayBuild < 70682 && gameEvent.data.GetAbilityLink() == 116 ||
                 replay.ReplayBuild >= 70682 && replay.ReplayBuild < 77525 && gameEvent.data.GetAbilityLink() == 112 ||
                 replay.ReplayBuild >= 77525 && gameEvent.data.GetAbilityLink() == 114);
        }

        public static bool SupportsCarriedObjectives(this Replay replay) => replay.MapAlternativeName switch
        {
            Constants.CarriedObjectiveMaps.BlackheartsBay => true,
            Constants.CarriedObjectiveMaps.TombOfTheSpiderQueen => true,
            Constants.CarriedObjectiveMaps.WarheadJunction => true,
            _ => false
        };

        public static int GetPlayerIndex(this Replay replay, Player player) => Array.IndexOf(replay.Players, player);

        public static bool IsPlayerReferenced(this Unit unit) => unit?.PlayerKilledBy != null || unit?.PlayerControlledBy != null;

        public static bool IsMercCapture(this Unit unit) => unit.Name.Equals("VolskayaMercCaptureSlab") ||
                                                            unit.Name.Equals("DragonballCaptureBeacon") ||
                                                            unit.Name.Equals("TownMercCampCaptureBeacon");

        public static bool IsWatchTowerCapture(this Unit unit) => unit.Name.Contains("WatchTower");

        public static bool IsMinion(this Unit unit) => unit.Name.EndsWith("Minion");

        public static bool IsHero(this Unit unit) => unit.Name.StartsWith("Hero") ||
                                                     unit.Name.Equals("LongboatRaidBoat") ||
                                                     unit.Name.Equals("MurkyRespawnEgg");

        public static bool IsMapObjective(this Unit unit) => unit.Group == Unit.UnitGroup.MapObjective ||
                                                             unit.Name.StartsWith("BossDuel") ||
                                                             unit.Name.Contains("Vehicle") ||
                                                             unit.Name.Equals("Shambler") ||
                                                             unit.Name.Equals("Seed") ||
                                                             unit.Name.Equals("WarheadSingle") ||
                                                             unit.Name.Equals("WarheadDropped") ||
                                                             unit.Name.Equals("HealingPulsePickup") ||
                                                             unit.Name.Equals("TurretPickup") ||
                                                             unit.Name.Equals("RegenGlobeNeutral") ||
                                                             unit.Name.Equals("Payload_Neutral") ||
                                                             unit.Name.Equals("GardenTerror") ||
                                                             unit.Name.Equals("HordeCavalry") ||
                                                             unit.Name.EndsWith("CaptureCage") ||
                                                             unit.IsCapturePoint();

        public static bool IsCapturePoint(this Unit unit) => unit.TimeSpanBorn == TimeSpan.Zero &&
                                                             unit.TimeSpanDied == null &&
                                                             unit.OwnerChangeEvents.Any() &&
                                                             !unit.Name.Contains("IconUnit") &&
                                                             !unit.Name.Equals("LootBannerSconce") &&
                                                             !unit.Name.Contains("Minimap");

        public static IEnumerable<TrackerEvent> GetCampCaptureEvents(this IEnumerable<TrackerEvent> trackerEvents, TimeSpan start, TimeSpan end) =>
            trackerEvents.Where(trackerEvent => trackerEvent.TimeSpan.IsWithin(start, end) &&
                                                trackerEvent.TrackerEventType ==
                                                ReplayTrackerEvents.TrackerEventType.StatGameEvent &&
                                                trackerEvent.Data.dictionary[0].blobText == "JungleCampCapture");


        public static bool IsCamp(this Unit unit) => unit.Group == Unit.UnitGroup.MercenaryCamp ||
                                                     unit.Name.StartsWith("Merc") ||
                                                     unit.Name.Equals("TerranHellbat") ||
                                                     unit.Name.Equals("TerranGoliath") ||
                                                     unit.Name.Equals("OverwatchTurret");
        public static bool IsBossCamp(this Unit unit) => unit.Name.Equals("TerranArchangelLaner") ||
                                                         unit.Name.Equals("SlimeBossLaner") ||
                                                         unit.Name.StartsWith("JungleGrave");

        public static bool IsStructure(this Unit unit) => unit.Team.HasValue && unit.Name.StartsWith("Town") || unit.IsCore();

        public static bool IsCore(this Unit unit) => unit.Name.Equals("KingsCore") || unit.Name.Equals("VanndarStormpike") || unit.Name.Equals("DrekThar");

        public static bool IsWithin(this TimeSpan value, TimeSpan start, TimeSpan end) => value >= start && value <= end;

        public static bool IsDead(this Unit unit, TimeSpan start, TimeSpan end) => unit.TimeSpanDied.HasValue && unit.TimeSpanDied.Value.IsWithin(start, end);

        public static bool IsAlive(this Unit unit, TimeSpan start, TimeSpan end) => unit.TimeSpanBorn <= start && (unit.TimeSpanDied == null || unit.TimeSpanDied.Value > end);

        public static Point GetSpawn(this Unit unit) => unit.PlayerControlledBy.HeroUnits[0].PointBorn;

        public static Point GetEnemySpawn(this Unit unit, Replay replay) => replay.Players[Array.IndexOf(replay.Players, unit.PlayerControlledBy) < 5 ? 9 : 0].HeroUnits[0].PointBorn;

        public static Hero? TryGetHero(this Player player) => Constants.Heroes.All.Find(hero => hero.Name.Equals(player.HeroId, StringComparison.InvariantCultureIgnoreCase));

        public static List<MatchAwardType> GetMatchAwards(this Replay replay) => replay.Players.SelectMany(p => p.ScoreResult.MatchAwards).Distinct().ToList();

        public static IEnumerable<string> GetText(this MatchAwardType matchAwardType) => Constants.MatchAwards[matchAwardType];

        public static IEnumerable<string> ToText(this IEnumerable<MatchAwardType> matchAwardTypes) => matchAwardTypes.SelectMany(mat => mat.GetText()).Distinct();
    }
}
