using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace HeroesReplay.Core.Shared
{
    public static class Extensions
    {
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> items) => items.OrderBy(i => Guid.NewGuid());

        /// <summary>
        /// The in-game timer at the top has a NEGATIVE offset of 610.
        /// This function removes the negative offset so that it can be used for normal calculations in the replay file.
        /// </summary>
        public static TimeSpan RemoveNegativeOffset(this TimeSpan timer)
        {
            var replayTimer = timer.Add(TimeSpan.FromSeconds(timer.Seconds + Constants.GAME_LOOPS_OFFSET) / Constants.GAME_LOOPS_PER_SECOND);
            return new TimeSpan(replayTimer.Days, replayTimer.Hours, replayTimer.Minutes, replayTimer.Seconds, milliseconds: 0);
        }

        public static TimeSpan AddNegativeOffset(this TimeSpan timer)
        {
            var topTimer = timer.Subtract(TimeSpan.FromSeconds(timer.Seconds + Constants.GAME_LOOPS_OFFSET) / Constants.GAME_LOOPS_PER_SECOND);
            return new TimeSpan(topTimer.Days, topTimer.Hours, topTimer.Minutes, topTimer.Seconds, milliseconds: 0);
        }

        public static TimeSpan ParseTimerHours(this string time)
        {
            return TimeSpan.ParseExact(time, Constants.Ocr.TIMESPAN_FORMAT_HOURS, CultureInfo.InvariantCulture);
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

        public static Bitmap GetResized(this Bitmap bmp, int zoom)
        {
            Bitmap result = new Bitmap(bmp.Width * zoom, bmp.Height * zoom);

            using (Graphics g = Graphics.FromImage(result))
            {
                g.DrawImage(bmp, 0, 0, bmp.Width * zoom, bmp.Height * zoom);
            }

            return result;
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
