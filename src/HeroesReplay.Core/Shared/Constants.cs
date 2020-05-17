namespace HeroesReplay.Core.Shared
{
    public static class Constants
    {
        public const string STORM_REPLAY_EXTENSION = ".StormReplay";
        public const string STORM_REPLAY_CACHED_FILE_NAME_SPLITTER = "_";
        public const string STORM_REPLAY_WILDCARD = "*.StormReplay";
        public const string STORM_REPLAY_INFO_FILE = "CurrentReplay.txt";
        public const string VARIABLES_WILDCARD = "*Variables.txt";
        public const string HEROES_PROCESS_NAME = "HeroesOfTheStorm_x64";
        public const string ENV_PREFIX = "HEROES_REPLAY_";

        public static class Ocr
        {
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