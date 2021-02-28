using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core.Services.YouTube;

using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.YouTube.Commands
{
    public class UploaderCommand : Command
    {
        public UploaderCommand() : base("uploader", $"Upload OBS recordings of replays")
        {
            Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }
               
        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (ServiceProvider provider = new ServiceCollection().AddYouTubeServices(cancellationToken).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                using (IServiceScope scope = provider.CreateScope())
                {
                    IYouTubeUploader uploader = scope.ServiceProvider.GetRequiredService<IYouTubeUploader>();
                    await uploader.ListenAsync();
                }
            }
        }
    }
}