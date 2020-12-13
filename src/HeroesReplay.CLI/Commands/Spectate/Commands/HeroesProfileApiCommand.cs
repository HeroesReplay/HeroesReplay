using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core;
using HeroesReplay.Core.Providers;

using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands
{
    public class HeroesProfileApiCommand : Command
    {
        public HeroesProfileApiCommand() : base("heroesprofile", "Access the HeroesProfile S3 bucket to download uploaded replays and spectate them.")
        {
            Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (IServiceScope scope = new ServiceCollection().AddCoreServices(cancellationToken, typeof(HeroesProfileProvider)).BuildServiceProvider().CreateScope())
            {
                SaltySadism saltySadism = scope.ServiceProvider.GetRequiredService<SaltySadism>();

                await saltySadism.RunAsync();
            }
        }
    }
}