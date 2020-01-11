using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay
{
    public static class Extensions
    {
        public static Task Delay(this AnalyzerResult result, CancellationToken token = default) => Task.Delay(result.Range, token);
    }
}
