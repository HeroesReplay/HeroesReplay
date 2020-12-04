using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core;
using HeroesReplay.Core.Replays;

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
                SaltySadism saltySadism = scope.ServiceProvider.GetRequiredService<SaltySadism>();

                await saltySadism.RunAsync();
            }
        }
    }
}