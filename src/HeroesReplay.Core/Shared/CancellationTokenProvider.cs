using System.Threading;

namespace HeroesReplay.Core.Shared
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