using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Spectator
{
    public static class Extensions
    {
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> items) => items.OrderBy(i => Guid.NewGuid());

        public static Task WaitCheckTime(this AnalyzerResult result, CancellationToken token = default) => Task.Delay(result.Duration, token);

        /// <summary>
        /// The in-game timer at the top has a NEGATIVE offset of 610.
        /// This function removes the negative offset so that it can be used for normal calculations in the replay file.
        /// </summary>
        public static TimeSpan RemoveNegativeOffset(this TimeSpan timer)
        {
            var bottomTimer = timer.Add(TimeSpan.FromSeconds(timer.Seconds + 610) / 16).Duration();
            return new TimeSpan(bottomTimer.Days, bottomTimer.Hours, bottomTimer.Minutes, bottomTimer.Seconds, milliseconds: 0);
        }

        public static TimeSpan AddNegativeOffset(this TimeSpan timer)
        {
            var topTimer = timer.Subtract(TimeSpan.FromSeconds(timer.Seconds + 610) / 16).Duration();
            return new TimeSpan(topTimer.Days, topTimer.Hours, topTimer.Minutes, topTimer.Seconds, milliseconds: 0);
        }

        public static TimeSpan ParseTimerHours(this string time) => TimeSpan.ParseExact(time, Constants.Ocr.TIMESPAN_FORMAT_HOURS, CultureInfo.InvariantCulture);

        public static TimeSpan ParseNegativeTimer(this string time) => TimeSpan.ParseExact(time, Constants.Ocr.TIMESPAN_MATCH_START_FORMAT, CultureInfo.CurrentCulture, TimeSpanStyles.AssumeNegative);

        public static TimeSpan ParseTimerMinutes(this string time)
        {
            string[] segments = time.Split(':');

            return new TimeSpan(days: 0, hours: 0, minutes: int.Parse(segments[0]), seconds: int.Parse(segments[1]));
        }

        public static IEnumerable<TSource> Interleave<TSource>(this IEnumerable<TSource> source1, IEnumerable<TSource> source2)
        {
            using (var enumerator1 = source1.GetEnumerator())
            {
                using (var enumerator2 = source2.GetEnumerator())
                {
                    bool continue1;
                    bool continue2;

                    do
                    {

                        if (continue1 = enumerator1.MoveNext())
                        {
                            yield return enumerator1.Current;
                        }

                        if (continue2 = enumerator2.MoveNext())
                        {
                            yield return enumerator2.Current;
                        }

                    }
                    while (continue1 || continue2);
                }
            }
        }
    }
}
