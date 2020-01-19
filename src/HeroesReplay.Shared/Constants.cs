using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static System.Environment;

namespace HeroesReplay.Shared
{
    public static class Constants
    {
        public const string STORM_INTERFACE_NAME = "AhliObs 0.66.StormInterface";
        public const string STORM_REPLAY_EXTENSION = ".StormReplay";
        public const string STORM_REPLAY_WILDCARD = "*.StormReplay";

        public static class Heroes
        {
            public static readonly Key[] KEYS_HEROES = { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0 };
            public static readonly Key[] KEYS_CONSOLE_PANEL = { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8 };

            public static readonly int[] TALENT_LEVELS = { 1, 4, 7, 10, 13, 16, 20 };

            public static List<Hero> All = typeof(Shared.Heroes).GetProperties(BindingFlags.Public).Where(p => p.PropertyType == typeof(Hero)).Select(p => p.GetValue(null)).OfType<Hero>().ToList();

            public static readonly string DOCUMENTS_HEROES_REPLAYS_PATH = Path.Combine(GetFolderPath(SpecialFolder.MyDocuments), "Heroes of the Storm", "Accounts");
            public static readonly string DOCUMENTS_HEROES_VARIABLES_PATH = Path.Combine(GetFolderPath(SpecialFolder.MyDocuments), "Heroes of the Storm", "Variables.txt");

            public const string HEROES_PROCESS_NAME = "HeroesOfTheStorm_x64";
            public const string HEROES_SWITCHER_PROCESS = "HeroesSwitcher_x64.exe";

            public static TimeSpan KILL_STREAK_TIMER = TimeSpan.FromSeconds(12);

            public static TimeSpan MAX_PENTA_KILL_STREAK_POTENTIAL = KILL_STREAK_TIMER * 4;
            public static TimeSpan MAX_QUAD_KILL_STREAK_POTENTIAL = KILL_STREAK_TIMER * 3;
            public static TimeSpan MAX_TRIPLE_KILL_STREAK_POTENTIAL = KILL_STREAK_TIMER * 2;
            public static TimeSpan MAX_MULTI_KILL_STREAK_POTENTIAL = KILL_STREAK_TIMER * 1;
        }

        public const int WM_KEYDOWN = 0x100;
        public const int WM_KEYUP = 0x101;
        public const int WM_CHAR = 0x102;
        public const int SRCCOPY = 0x00CC0020;

        public static class Cmd
        {
            public const string ARG_REPLAY = "replay";
            public const string ARG_LAUNCH = "launch";
            public const string ARG_BNET_INSTALL = "bnet";
            public const string ARG_REPLAYS_DIR = "replays";
        }

        public static class Ocr
        {
            public const string LOADING_SCREEN_TEXT = "WELCOME TO";
            public const string GAME_RUNNING_TEXT = "Game is running.";
            public const string PLAY_BUTTON_TEXT = "PLAY";
            public const string SHOP_HEROES_TEXT = "Shop Heroes of the Storm";

            public const string TIMER_COLON = ":";
            public const int TIMER_HOURS = 3;
            public const int TIMER_MINUTES = 2;

            public const string TIMESPAN_FORMAT_HOURS = "hh\\:mm\\:ss";
            public const string TIMESPAN_MATCH_START_FORMAT = "\\-mm\\:ss";
            public const string TIMESPAN_FORMAT_MINUTES = "mm\\:ss";
        }

        public static class Bnet
        {
            public const string BATTLE_NET_LAUNCHER_EXE = "Battle.net Launcher.exe";
            public const string BATTLE_NET_EXE = "Battle.net.exe";
            public const string BATTLE_NET_PROCESS_NAME = "Battle.net";
            public const string BATTLE_NET_SELECT_HEROES_ARG = "--game heroes";
            public const string BATTLE_NET_DEFAULT_INSTALL_PATH = "C:\\Program Files (x86)\\Battle.net";
        }
    }
}
