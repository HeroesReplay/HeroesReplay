using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HeroesReplay
{
    public class EntryPoint
    {
        private readonly ILogger<EntryPoint> logger;
        private readonly ConsoleService consoleService;
        private readonly ReplayConsumer replayConsumer;

        public EntryPoint(ILogger<EntryPoint> logger, ConsoleService consoleService, ReplayConsumer replayConsumer)
        {
            this.logger = logger;
            this.consoleService = consoleService;
            this.replayConsumer = replayConsumer;
        }

        public async Task RunHeroesReplayAsync()
        {
            consoleService.WriteHello();

            if (consoleService.IsValid())
            {
                if (consoleService.HasReplayArgument)
                {
                    await replayConsumer.RunAsync(consoleService.ReplayFile, consoleService.LaunchGame);
                }
                else if(consoleService.HasReplayDirectoryArgument)
                {
                    await replayConsumer.RunAsync();
                }
            }

            consoleService.WriteGoodbye();
        }

    }
}