using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration
{
    public class OCRSettings
    {
        public IEnumerable<string> HomeScreenText { get; set; }
        public IEnumerable<string> LoadingScreenText { get; set; }
        public string TimerSeperator { get; set; }
        public string TimerNegativePrefix { get; set; }
        public int TimerHours { get; set; }
        public int TimerMinutes { get; set; }
        public TimeSpan CheckSleepDuration { get; set; }
        public string TimeSpanFormatHours { get; set; }
        public string TimeSpanFormatMatchStart { get; set; }
        public string TimeSpanFormatMinutes { get; set; }
    }
}