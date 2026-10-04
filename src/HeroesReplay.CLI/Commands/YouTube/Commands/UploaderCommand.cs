using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Telemetry;
using HeroesReplay.Core.YouTube;
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
            .BuildHeroesReplayProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        using Activity ready = HeroesReplayTelemetry.StartSpan("heroesreplay.service.ready");
        using IServiceScope scope = provider.CreateScope();
        IYouTubeUploader uploader = scope.ServiceProvider.GetRequiredService<IYouTubeUploader>();
        using ServiceHeartbeat heartbeat = ServiceHeartbeat.StartFromEnvironment(
            "youtube",
            scope.ServiceProvider.GetRequiredService<AppSettings>().ServiceHealth,
            stop.Token
        );
        await uploader.ListenAsync();
    }
}
