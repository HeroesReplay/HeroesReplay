using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands
{
    public class HotsApiCommand : Command
    {
        public HotsApiCommand() : base("hotsapi", "Access the HotsApi database to download uploaded replays and spectate them.")
        {
            Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (IServiceScope scope = new ServiceCollection().AddCoreServices(cancellationToken, typeof(HotsApiProvider)).BuildServiceProvider().CreateScope())
            {
                ReplayConsumer stormReplayConsumer = scope.ServiceProvider.GetRequiredService<ReplayConsumer>();

                await stormReplayConsumer.RunAsync();
            }
        }
    }
}