using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay
{
    public static class Extensions
    {
        public static Task Delay(this AnalyzerResult result, CancellationToken token = default) => Task.Delay(result.Range, token);

        /// <summary>
        /// The in-game timer at the top has a NEGATIVE offset of 610.
        /// This function removes the negative offset so that it can be used for normal calculations in the replay file.
        /// </summary>
        public static TimeSpan AddPositiveOffset(this TimeSpan timer) => timer.Add(TimeSpan.FromSeconds(timer.Seconds + 610) / 16);
    }
}
