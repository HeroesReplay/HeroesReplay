using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core;
using HeroesReplay.Core.Providers;

using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands
{
    public class SpectateHeroesProfileApiCommand : Command
    {
        public SpectateHeroesProfileApiCommand() : base("heroesprofile", "Access the HeroesProfile S3 bucket to download uploaded replays and spectate them.")
        {
            Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (ServiceProvider provider = new ServiceCollection().AddSpectateServices(cancellationToken, typeof(HeroesProfileProvider)).BuildServiceProvider())
            {
                using (IServiceScope scope = provider.CreateScope())
                {
                    IHeroesReplayEngine engine = scope.ServiceProvider.GetRequiredService<IHeroesReplayEngine>();
                    await engine.RunAsync();
                }
            }
        }
    }
}