using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core;
using HeroesReplay.Core.Providers;

using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands
{
    public class SpectateDirectoryCommand : Command
    {
        public SpectateDirectoryCommand() : base("directory", $"The directory that contains .StormReplay files.")
        {
            Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (IServiceScope scope = new ServiceCollection().AddCoreServices(cancellationToken, typeof(ReplayDirectoryProvider)).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }).CreateScope())
            {
                SaltySadism saltySadism = scope.ServiceProvider.GetRequiredService<SaltySadism>();

                await saltySadism.RunAsync();
            }
        }
    }
}