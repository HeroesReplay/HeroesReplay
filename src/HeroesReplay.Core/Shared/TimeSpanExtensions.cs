using System;
using System.Diagnostics.Contracts;
using System.Globalization;

namespace HeroesReplay.Core.Shared
{
    public static class TimeSpanExtensions
    {
        public static TimeSpan ParseTimerHours(this string time, string format) => TimeSpan.ParseExact(time, format, CultureInfo.CurrentCulture);

        public static TimeSpan ParseNegativeTimerMinutes(this string time, string format) => TimeSpan.ParseExact(time, format, CultureInfo.CurrentCulture, TimeSpanStyles.AssumeNegative);

        public static TimeSpan ParsePositiveTimerMinutes(this string time, string seperator)
        {
            if (time == null)
                throw new ArgumentNullException(nameof(time));

            string[] segments = time.Split(seperator);
            return new TimeSpan(days: 0, hours: 0, minutes: int.Parse(segments[0]), seconds: int.Parse(segments[1]));
        }

        public static TimeSpan? RemoveNegativeOffset(this TimeSpan? timer, int gameLoopsOffset, int gameLoopsPerSecond)
        {
            if (timer != null)
            {
                var offset = (timer.Value.Seconds + gameLoopsOffset) / gameLoopsPerSecond;
                var replayTimer = timer.Value.Add(TimeSpan.FromSeconds(offset));
                return new TimeSpan?(new TimeSpan(replayTimer.Days, replayTimer.Hours, replayTimer.Minutes, replayTimer.Seconds, milliseconds: 0));
            }

            return null;
        }

        public static TimeSpan AddNegativeOffset(this TimeSpan timer, int gameLoopsOffset, int gameLoopsPerSecond)
        {
            var offset = (timer.Seconds + gameLoopsOffset) / gameLoopsPerSecond;
            var replayTimer = timer.Subtract(TimeSpan.FromSeconds(offset));
            return new TimeSpan(replayTimer.Days, replayTimer.Hours, replayTimer.Minutes, replayTimer.Seconds, milliseconds: 0);
        }
    }
}
