using System;
using System.Globalization;

namespace HeroesReplay.Core.Shared
{
    public static class TimeSpanExtensions
    {
        public static TimeSpan ParseTimerHours(this string time)
        {
            return TimeSpan.ParseExact(time, Constants.Ocr.TIMESPAN_FORMAT_HOURS, CultureInfo.CurrentCulture);
        }

        public static TimeSpan ParseNegativeTimerMinutes(this string time)
        {
            return TimeSpan.ParseExact(time, Constants.Ocr.TIMESPAN_MATCH_START_FORMAT, CultureInfo.CurrentCulture, TimeSpanStyles.AssumeNegative);
        }

        public static TimeSpan ParsePositiveTimerMinutes(this string time)
        {
            string[] segments = time.Split(Constants.Ocr.TIMER_HRS_MINS_SECONDS_SEPERATOR);

            return new TimeSpan(days: 0, hours: 0, minutes: int.Parse(segments[0]), seconds: int.Parse(segments[1]));
        }
    }
}
