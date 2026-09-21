using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Services.Processes;
using HeroesReplay.Core.Services.YouTube;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.YouTube.Commands;

public class UploaderCommand : Command
{
    public UploaderCommand()
        : base("uploader", "Upload OBS recordings of replays")
    {
        SetAction(
            async (parseResult, cancellationToken) =>
            {
                await CommandAsync(cancellationToken);
            }
        );
    }

    protected async Task CommandAsync(CancellationToken cancellationToken)
    {
        using ServiceStopLink stop = ServiceStopFile.Link(cancellationToken);
        using ServiceProvider provider = new ServiceCollection()
            .AddYouTubeServices(stop.Token)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        using IServiceScope scope = provider.CreateScope();
        IYouTubeUploader uploader = scope.ServiceProvider.GetRequiredService<IYouTubeUploader>();
        await uploader.ListenAsync();
    }
}
