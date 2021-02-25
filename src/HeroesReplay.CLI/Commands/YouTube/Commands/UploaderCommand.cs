using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.YouTube.Commands
{
    public class UploaderCommand : Command
    {
        public UploaderCommand() : base("uploader", $"Upload OBS recordings of replays")
        {
            Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }

        // https://developers.google.com/youtube/v3/code_samples/dotnet#upload_a_video
        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (ServiceProvider provider = new ServiceCollection().AddYouTubeServices(cancellationToken).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                using (IServiceScope scope = provider.CreateScope())
                {
                    using (var waiter = new ManualResetEventSlim())
                    {
                         //ITwitchBot twitchBot = scope.ServiceProvider.GetRequiredService<ITwitchBot>();
                         //await twitchBot.InitializeAsync();
                         //waiter.Wait(cancellationToken);
                    }
                }
            }
        }
    }
}