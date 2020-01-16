using System.Threading;

namespace HeroesReplay.Spectator
{
    public class CancellationTokenProvider
    {
        public CancellationToken Token { get; }

        public CancellationTokenProvider(CancellationToken token)
        {
            Token = token;
        }
    }
}