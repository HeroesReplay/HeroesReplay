using System;
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
    }
}
