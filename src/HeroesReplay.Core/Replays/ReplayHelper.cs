using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Shared
{
    public class ReplayHelper
    {
        private readonly ILogger<ReplayHelper> logger;
        private readonly GameDataService gameDataService;
        private readonly Settings settings;

        public ReplayHelper(ILogger<ReplayHelper> logger, IOptions<Settings> settings, GameDataService gameDataService)
        {
            this.logger = logger;
            this.gameDataService = gameDataService;
            this.settings = settings.Value;
        }
        
        // public IEnumerable<string> GetTextForMatchAwards(IEnumerable<MatchAwardType> matchAwardTypes) => matchAwardTypes.SelectMany(mat => MatchAwards[mat]).Distinct();
        //public Dictionary<MatchAwardType, string[]> MatchAwards = new Dictionary<MatchAwardType, string[]>
        //{
        //    {MatchAwardType.ClutchHealer, new[] {"Clutch Healer"} },
        //    {MatchAwardType.HatTrick, new[] {"Hat Trick"}},
        //    {MatchAwardType.HighestKillStreak, new[] {"Dominator"}},
        //    {MatchAwardType.MostAltarDamage, new[] {"Cannoneer"}},
        //    {MatchAwardType.MostCoinsPaid, new[] {"Moneybags"}},
        //    {MatchAwardType.MostCurseDamageDone, new[] {"Master of the Curse"}},
        //    {MatchAwardType.MostDamageDoneToZerg, new[] {"Zerg Crusher"}},
        //    {MatchAwardType.MostDamageTaken, new[] {"Bulwark"}},
        //    {MatchAwardType.MostDamageToMinions, new[] {"Guardian Slayer"}},
        //    {MatchAwardType.MostDaredevilEscapes, new[] {"Daredevil"}},
        //    {MatchAwardType.MostDragonShrinesCaptured, new[] {"Shriner"}},
        //    {MatchAwardType.MostEscapes, new[] {"Escape Artist"}},
        //    {MatchAwardType.MostGemsTurnedIn, new[] {"Jeweler"}},
        //    {MatchAwardType.MostHealing, new[] {"Main Healer"}},
        //    {MatchAwardType.MostHeroDamageDone, new[] {"Painbringer"}},
        //    {MatchAwardType.MostImmortalDamage, new[] {"Immortal Slayer"}},
        //    {MatchAwardType.MostInterruptedCageUnlocks, new[] {"Loyal Defender"}},
        //    {MatchAwardType.MostKills, new[] {"Finisher"}},
        //    {MatchAwardType.MostMercCampsCaptured, new[] {"Headhunter"}},
        //    {MatchAwardType.MostNukeDamageDone, new[] {"Da Bomb"}},
        //    {MatchAwardType.MostProtection, new[] {"Protector"}},
        //    {MatchAwardType.MostRoots, new[] {"Trapper"}},
        //    {MatchAwardType.MostSeedsCollected, new[] {"Seed Collector"}},
        //    {MatchAwardType.MostSiegeDamageDone, new[] {"Siege Master"}},
        //    {MatchAwardType.MostSilences, new[] {"Silencer"}},
        //    {MatchAwardType.MostSkullsCollected, new[] {"Skull Collector"}},
        //    {MatchAwardType.MostStuns, new[] {"Stunner"}},
        //    {MatchAwardType.MostTeamfightDamageTaken, new[] {"Guardian"}},
        //    {MatchAwardType.MostTeamfightHealingDone, new[] {"Combat Medic"}},
        //    {MatchAwardType.MostTeamfightHeroDamageDone, new[] {"Scrapper"}},
        //    {MatchAwardType.MostTimeInTemple, new[] {"Temple Master"}},
        //    {MatchAwardType.MostTimeOnPoint, new[] {"Point Guard"}},
        //    {MatchAwardType.MostTimePushing, new[] {"Pusher"}},
        //    {MatchAwardType.MostVengeancesPerformed, new[] {"Avenger"}},
        //    {MatchAwardType.MostXPContribution, new[] {"Experienced"}},
        //    {MatchAwardType.MVP, new[] {"MVP"}},
        //    {MatchAwardType.ZeroDeaths, new[] {"Sole Survivor"}},
        //    {MatchAwardType.ZeroOutnumberedDeaths, new[] {"Team Player"}}
        //};

        public ParseOptions ReplayParseOptions = new ParseOptions
        {
            AllowPTR = false,
            IgnoreErrors = true,
            ShouldParseDetailedBattleLobby = true,
            ShouldParseEvents = true,
            ShouldParseMessageEvents = true,
            ShouldParseMouseEvents = true,
            ShouldParseStatistics = true,
            ShouldParseUnits = true
        };

        public int TryGetReplayId(StormReplay stormReplay)
        {
            try
            {
                return int.Parse(Path.GetFileName(stormReplay.Path).Split(Constants.STORM_REPLAY_CACHED_FILE_NAME_SPLITTER)[0]);
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not parse the replay ID from {stormReplay.Path}");

                throw;
            }
        }

        public TimeSpan RemoveNegativeOffset(TimeSpan timer)
        {
            var replayTimer = timer.Add(TimeSpan.FromSeconds(timer.Seconds + settings.GameLoopsOffset) / settings.GameLoopsPerSecond);
            return new TimeSpan(replayTimer.Days, replayTimer.Hours, replayTimer.Minutes, replayTimer.Seconds, milliseconds: 0);
        }

        public TimeSpan AddNegativeOffset(TimeSpan timer)
        {
            var topTimer = timer.Subtract(TimeSpan.FromSeconds(timer.Seconds + settings.GameLoopsOffset) / settings.GameLoopsPerSecond);
            return new TimeSpan(topTimer.Days, topTimer.Hours, topTimer.Minutes, topTimer.Seconds, milliseconds: 0);
        }

        public int GetAbilityLink(TrackerEventStructure structure)
        {
            return Convert.ToInt32(structure?.array[1]?.array[0]?.unsignedInt.GetValueOrDefault()); // m_abilLink
        }

        public int GetAbilityCmdIndex(TrackerEventStructure trackerEvent)
        {
            return Convert.ToInt32(trackerEvent.array[1]?.array[1]?.unsignedInt.GetValueOrDefault()); // m_abilCmdIndex
        }

        public bool IsTaunt(Replay replay, GameEvent gameEvent)
        {
            return gameEvent.eventType == GameEventType.CCmdEvent && GetAbilityCmdIndex(gameEvent.data) == 4 &&
                (
                    GetAbilityLink(gameEvent.data) == 19 && replay.ReplayBuild < 68740 ||
                    GetAbilityLink(gameEvent.data) == 22 && replay.ReplayBuild >= 68740
                );
        }

        public bool IsDance(Replay replay, GameEvent gameEvent)
        {
            return gameEvent.eventType == GameEventType.CCmdEvent && GetAbilityCmdIndex(gameEvent.data) == 3 &&
                (
                    GetAbilityLink(gameEvent.data) == 19 && replay.ReplayBuild < 68740 ||
                    GetAbilityLink(gameEvent.data) == 22 && replay.ReplayBuild >= 68740
                );
        }

        public bool IsHearthStone(Replay replay, GameEvent gameEvent)
        {
            return gameEvent.eventType == GameEventType.CCmdEvent &&
                (
                    (replay.ReplayBuild < 61872 && GetAbilityLink(gameEvent.data) == 200) ||
                    (replay.ReplayBuild >= 61872 && replay.ReplayBuild < 68740 && GetAbilityLink(gameEvent.data) == 119) ||
                    (replay.ReplayBuild >= 68740 && replay.ReplayBuild < 70682 && GetAbilityLink(gameEvent.data) == 116) ||
                    (replay.ReplayBuild >= 70682 && replay.ReplayBuild < 77525 && GetAbilityLink(gameEvent.data) == 112) ||
                    (replay.ReplayBuild >= 77525 && GetAbilityLink(gameEvent.data) == 114)
                );
        }

        public bool IsNearEnd(Replay replay, TimeSpan timer)
        {
            return replay.Units
                .Any(unit => IsCore(unit) && unit.TimeSpanDied.HasValue &&
                                             unit.TimeSpanDied.Value.Add(TimeSpan.FromSeconds(5)) >= timer &&
                                             unit.TimeSpanDied.Value.Add(TimeSpan.FromSeconds(-5)) <= timer);
        }

        public bool IsCarriedObjectiveMap(Replay replay) => settings.CarriedObjectiveMaps.Contains(replay.MapAlternativeName);

        public bool IsCamp(Unit unit) => unit.Group == Unit.UnitGroup.MercenaryCamp || unit.Name.StartsWith("Merc") || settings.CampUnitNames.Contains(unit.Name);

        public bool IsHero(Unit unit) => unit.Name.StartsWith("Hero") || unit.Name.Equals("LongboatRaidBoat") || unit.Name.Equals("MurkyRespawnEgg");

        public int GetPlayerIndex(Replay replay, Player player) => Array.IndexOf(replay.Players, player);

        public bool IsWatchTowerCapture(Unit unit) => unit.Name.Contains("WatchTower");

        public bool IsMinion(Unit unit) => unit.Group == Unit.UnitGroup.Minions || unit.Name.EndsWith("Minion");

        public bool IsMapObjective(Unit unit) => unit.Group == Unit.UnitGroup.MapObjective ||
                                                             unit.Name.StartsWith("BossDuel") ||
                                                             unit.Name.Contains("Vehicle") ||
                                                             settings.MapObjectiveUnitNames.Contains(unit.Name) ||
                                                             unit.Name.EndsWith("CaptureCage") ||
                                                             IsCapturePoint(unit);

        // Turrets, Camps, Bosses, Vision, Pirates
        public bool IsCapturePoint(Unit unit)
        {
            return unit.TimeSpanBorn == TimeSpan.Zero && // They are born instantly
                   unit.TimeSpanDied == null &&  // Capture points never die
                   unit.OwnerChangeEvents.Any() && // They do have owner change events (team red, team blue)
                          !unit.Name.Contains("IconUnit") &&
                          !unit.Name.Equals("LootBannerSconce") &&
                          !unit.Name.Contains("Minimap");
        }

        public IEnumerable<TrackerEvent> GetCampCaptureEvents(IEnumerable<TrackerEvent> trackerEvents, TimeSpan start, TimeSpan end)
        {
            return trackerEvents.Where(trackerEvent =>
                IsWithin(trackerEvent.TimeSpan, start, end) &&
                trackerEvent.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent &&
                trackerEvent.Data.dictionary[0].blobText == "JungleCampCapture");
        }

        public bool IsBossCamp(Unit unit) => settings.BossUnitNames.Contains(unit.Name);

        public bool IsStructure(Unit unit) => unit.Team.HasValue && unit.Name.StartsWith("Town") || IsCore(unit);

        public bool IsCore(Unit unit) => settings.CoreUnitNames.Contains(unit.Name);

        public bool IsWithin(TimeSpan value, TimeSpan start, TimeSpan end) => value >= start && value <= end;

        public bool IsDead(Unit unit, TimeSpan start, TimeSpan end) => unit.TimeSpanDied.HasValue && IsWithin(unit.TimeSpanDied.Value, start, end);

        public bool IsAlive(Unit unit, TimeSpan start, TimeSpan end) => unit.TimeSpanBorn <= start && (unit.TimeSpanDied == null || unit.TimeSpanDied.Value > end);

        public Point GetSpawn(Unit unit) => unit.PlayerControlledBy.HeroUnits[0].PointBorn;

        public Point GetEnemySpawn(Unit unit, Replay replay)
        {
            const int FIRST_PLAYER_TEAM_A = 0;
            const int LAST_PLAYER_TEAM_B = 9;

            return replay.Players[Array.IndexOf(replay.Players, unit.PlayerControlledBy) < 5 ? LAST_PLAYER_TEAM_B : FIRST_PLAYER_TEAM_A].HeroUnits[0].PointBorn;
        }

        public Hero? TryGetHero(Player player)
        {
            return gameDataService.Heroes.Find(hero => hero.Name.Equals(player.HeroId, StringComparison.OrdinalIgnoreCase));
        }
    }
}