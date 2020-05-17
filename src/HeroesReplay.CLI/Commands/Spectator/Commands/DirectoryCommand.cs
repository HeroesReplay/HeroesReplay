using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands
{
    public class DirectoryCommand : Command
    {
        public DirectoryCommand() : base("directory", $"The directory that contains {Constants.STORM_REPLAY_EXTENSION} files.")
        {
            Handler = CommandHandler.Create<CancellationToken>(ActionAsync);
        }

        protected async Task ActionAsync(CancellationToken cancellationToken)
        {
            using (IServiceScope scope = new ServiceCollection().AddCoreServices(cancellationToken, typeof(DirectoryProvider)).BuildServiceProvider().CreateScope())
            {
                ReplayConsumer stormReplayConsumer = scope.ServiceProvider.GetRequiredService<ReplayConsumer>();

                await stormReplayConsumer.RunAsync();
            }
        }
    }
}