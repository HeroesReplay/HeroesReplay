using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core.Services;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;

using Microsoft.Extensions.Logging;

namespace HeroesReplay.Service.Obs
{
    public class ObsService : BackgroundService
    {
        private readonly ILogger<ObsService> logger;
        private readonly IObsEntryMonitor obsEntryMonitor;

        public ObsService(ILogger<ObsService> logger, IObsEntryMonitor obsEntryMonitor, CancellationTokenSource cts) : base(cts)
        {
            this.logger = logger;
            this.obsEntryMonitor = obsEntryMonitor;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await obsEntryMonitor.ListenAsync();
            }
        }
    }
}
