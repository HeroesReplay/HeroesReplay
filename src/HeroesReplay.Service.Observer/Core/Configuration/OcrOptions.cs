using System;
using System.Collections.Generic;

namespace HeroesReplay.Service.Spectator.Core.Configuration
{
    public class OcrOptions
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