using System.Threading;

namespace HeroesReplay.Shared
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