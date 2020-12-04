using Heroes.ReplayParser;

using static PInvoke.User32;

namespace HeroesReplay.Core.Shared
{
    public static class Constants
    {
        public static ParseOptions ParseOptions = new ParseOptions { ShouldParseEvents = true, ShouldParseMouseEvents = false, ShouldParseMessageEvents = false, ShouldParseStatistics = true, ShouldParseUnits = true, ShouldParseDetailedBattleLobby = false };

        public static readonly VirtualKey[] KeysHeroes =
        {
            VirtualKey.VK_KEY_1, VirtualKey.VK_KEY_2, VirtualKey.VK_KEY_3, VirtualKey.VK_KEY_4, VirtualKey.VK_KEY_5, VirtualKey.VK_KEY_6, VirtualKey.VK_KEY_7, VirtualKey.VK_KEY_8, VirtualKey.VK_KEY_9, VirtualKey.VK_KEY_0
        };

        public static readonly VirtualKey[] KeysPanels =
        {
            VirtualKey.VK_KEY_1, VirtualKey.VK_KEY_2, VirtualKey.VK_KEY_3, VirtualKey.VK_KEY_4, VirtualKey.VK_KEY_5, VirtualKey.VK_KEY_6, VirtualKey.VK_KEY_7, VirtualKey.VK_KEY_8
        };

        public static string[] Bosses = new[] { "TerranArchangelLaner", "SlimeBossLaner", "JungleGrave" };
        public static string[] OtherCamps = new[] { "TerranHellbat", "TerranGoliath", "OverwatchTurret", "Merc" };
        public static string[] IgnoreUnits = new[] { "LootBannerSconce", "IconUnit", "PathingBlocker" };
        public static string[] MapObjectives = new[] { "Shambler", "Seed", "WarheadSingle", "WarheadDropped", "HealingPulsePickup", "TurretPickup", "RegenGlobeNeutral", "Payload_Neutral", "GardenTerror", "HordeCavalry" };

        public static class Weights
        {
            public const double Roaming = 2000;
            public const double KillingMinions = 2500;
            public const double NearCaptureBeacon = 3000;
            public const double DestroyStructure = 4000;
            public const double MercClear = 6000;
            public const double CampCapture = 7000;
            public const double BossCapture = 8000;
            public const double TauntingEmote = 8100;
            public const double TauntingDance = 8200;
            public const double TauntingBStep = 8300;
            public const double MapObjective = 8500;
            public const double NearEnemyCore = 8800;
            public const double NearEnemyHero = 9000;
            public const double PlayerDeath = 9500;
            public const double PlayerKill = 10000;
        }

        public const string STORM_REPLAY_EXTENSION = ".StormReplay";
        public const string STORM_REPLAY_CACHED_FILE_NAME_SPLITTER = "_";
        public const string STORM_REPLAY_WILDCARD = "*.StormReplay";
        public const string STORM_REPLAY_INFO_FILE = "CurrentReplay.txt";
        public const string VARIABLES_WILDCARD = "*Variables.txt";
        public const string HEROES_PROCESS_NAME = "HeroesOfTheStorm_x64";
        public const string BATTLENET_PROCESS_NAME = "Battle.net";
        public const string ENV_PREFIX = "HEROES_REPLAY_";

        public static class Ocr
        {
            public static string[] HOME_SCREEN_TEXT = new[] { "PLAY", "COLLECTION", "LOOT", "WATCH" };
            public const string LOADING_SCREEN_TEXT = "WELCOME TO";
            public const char TIMER_HRS_MINS_SECONDS_SEPERATOR = ':';
            public const char TIMER_NEGATIVE_PREFIX = '-';
            public const string TIMESPAN_FORMAT_HOURS = "hh\\:mm\\:ss";
            public const string TIMESPAN_MATCH_START_FORMAT = "\\-mm\\:ss";
            public const string TIMESPAN_FORMAT_MINUTES = "mm\\:ss";
            public const int TIMER_HOURS = 3;
            public const int TIMER_MINUTES = 2;
        }
    }
}