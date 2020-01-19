using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Spectator
{
    public static class Extensions
    {
        public static Task WaitCheckTime(this AnalyzerResult result, CancellationToken token = default) => Task.Delay(result.Duration, token);
    }
}