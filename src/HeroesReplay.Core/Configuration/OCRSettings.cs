using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Shared
{
    public record OCRSettings
    {
        public IEnumerable<string> HomeScreenText { get; init; }
        public IEnumerable<string> LoadingScreenText { get; init; }
        public string TimerSeperator { get; init; }
        public string TimerNegativePrefix { get; init; }
        public int TimerHours { get; init; }
        public int TimerMinutes { get; init; }
        public TimeSpan CheckSleepDuration { get; init; }
        public string TimeSpanFormatHours { get; init; }
        public string TimeSpanFormatMatchStart { get; init; }
        public string TimeSpanFormatMinutes { get; init; }
    }
}