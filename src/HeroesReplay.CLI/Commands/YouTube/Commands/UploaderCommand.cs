using System;
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
using Microsoft.Extensions.Logging;

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
        AppSettings settings = scope.ServiceProvider.GetRequiredService<AppSettings>();
        // A token refresh before ready, then on an interval: no YouTube quota (#305).
        var probes = new ServiceDependencyMonitor(
            new YouTubeOAuthProbe(settings),
            settings.ServiceHealth,
            scope.ServiceProvider.GetRequiredService<ILogger<ServiceDependencyMonitor>>()
        );
        ServiceDependencyResult dependency = await probes.FirstAsync(stop.Token);
        using ServiceHeartbeat heartbeat = ServiceHeartbeat.StartFromEnvironment(
            "youtube",
            settings.ServiceHealth,
            stop.Token,
            dependency
        );
        using IDisposable probing = probes.Watch(stop.Token);
        await uploader.ListenAsync();
    }
}
