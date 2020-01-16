using System.Threading;

namespace HeroesReplay.Spectator
{
    public class CancellationTokenProvider
    {
        public CancellationToken Token { get; set; }

        public CancellationTokenProvider(CancellationToken token = default)
        {
            Token = token;
        }
    }
}