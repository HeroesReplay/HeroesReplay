using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core;
using HeroesReplay.Core.Replays;

using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands
{
    public class FileCommand : Command
    {
        public FileCommand() : base("file", "The individual .StormReplay file to spectate.")
        {
             Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (IServiceScope scope = new ServiceCollection().AddCoreServices(cancellationToken, typeof(ReplayFileProvider)).BuildServiceProvider().CreateScope())
            {
                SaltySadism saltySadism = scope.ServiceProvider.GetRequiredService<SaltySadism>();

                await saltySadism.RunAsync();
            }
        }
    }
}