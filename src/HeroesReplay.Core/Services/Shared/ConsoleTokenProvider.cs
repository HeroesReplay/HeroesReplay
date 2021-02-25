using System.Threading;

namespace HeroesReplay.Core.Services.Shared
{
    public class ConsoleTokenProvider
    {
        public CancellationToken Token { get; set; }

        public ConsoleTokenProvider(CancellationToken token = default)
        {
            Token = token;
        }
    }
}