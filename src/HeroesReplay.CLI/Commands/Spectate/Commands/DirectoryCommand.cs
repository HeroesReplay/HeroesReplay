using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core;
using HeroesReplay.Core.Providers;
using HeroesReplay.Core.Runner;

using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands
{
    public class DirectoryCommand : Command
    {
        public DirectoryCommand() : base("directory", $"The directory that contains .StormReplay files.")
        {
            Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (IServiceScope scope = new ServiceCollection().AddCoreServices(cancellationToken, typeof(ReplayDirectoryProvider)).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }).CreateScope())
            {
                IHeroesToolChestData heroesDataLoader = scope.ServiceProvider.GetRequiredService<IHeroesToolChestData>();
                await heroesDataLoader.LoadDataAsync();

                SaltySadism saltySadism = scope.ServiceProvider.GetRequiredService<SaltySadism>();

                await saltySadism.RunAsync();
            }
        }
    }
}