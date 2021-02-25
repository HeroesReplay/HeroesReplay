using System.Threading;

namespace HeroesReplay.Core.Services.Shared
{
    public class ProcessCancellationTokenProvider
    {
        public CancellationToken Token { get; set; }

        public ProcessCancellationTokenProvider(CancellationToken token = default)
        {
            Token = token;
        }
    }
}