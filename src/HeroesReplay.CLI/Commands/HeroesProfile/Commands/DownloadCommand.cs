using System;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands.HeroesProfile.Commands;

public class DownloadCommand : Command
{
    public DownloadCommand()
        : base(
            "download",
            "List and download Storm League replays into Data\\Standard. Does not launch the game."
        )
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
        using ServiceProvider provider = AddServices(new ServiceCollection(), stop.Token)
            .BuildHeroesReplayProvider();
        using Activity ready = HeroesReplayTelemetry.StartSpan("heroesreplay.service.ready");
        using IServiceScope scope = provider.CreateScope();
        HeroesProfileProvider downloader =
            scope.ServiceProvider.GetRequiredService<HeroesProfileProvider>();
        ILogger<DownloadCommand> logger = scope.ServiceProvider.GetRequiredService<
            ILogger<DownloadCommand>
        >();
        using ServiceHeartbeat heartbeat = ServiceHeartbeat.StartFromEnvironment(
            "download",
            scope.ServiceProvider.GetRequiredService<AppSettings>().ServiceHealth,
            stop.Token
        );
        int failures = 0;
        while (!stop.Token.IsCancellationRequested)
        {
            OperatingMode mode = OutageMode.Decide(
                internetUp: failures == 0,
                downFor: TimeSpan.FromSeconds(15 * (long)failures),
                stableFor: TimeSpan.Zero
            );
            if (!OutageMode.MayDownload(mode))
            {
                logger.LogWarning(
                    "Heroes Profile download is paused ({Mode}). The downloader stays up.",
                    mode
                );
                try
                {
                    await Task.Delay(OutageMode.PauseDelay, stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            bool downloaded = false;
            try
            {
                downloaded = await downloader.DownloadNextAsync().ConfigureAwait(false);
                ServiceHeartbeat.RecordWork();
                if (downloaded)
                {
                    failures = 0;
                }
            }
            catch (OperationCanceledException) when (stop.Token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                failures++;
                OperatingMode failed = OutageMode.Decide(
                    internetUp: false,
                    downFor: TimeSpan.FromSeconds(15 * (long)failures),
                    stableFor: TimeSpan.Zero
                );
                logger.LogError(
                    e,
                    "Heroes Profile download failed ({Mode}). The downloader stays up.",
                    failed
                );
            }

            try
            {
                await Task.Delay(
                        downloaded ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(15),
                        stop.Token
                    )
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>The services this command resolves: the Twitch services and the downloader.</summary>
    public static IServiceCollection AddServices(
        IServiceCollection services,
        CancellationToken cancellationToken
    ) =>
        services
            .AddTwitchServices(cancellationToken, "heroesreplay-download")
            .AddSingleton<ReplayLoader>()
            .AddSingleton<IReplayLoader>(sp => sp.GetRequiredService<ReplayLoader>())
            .AddSingleton<ReplayHelper>()
            .AddSingleton<IReplayHelper>(sp => sp.GetRequiredService<ReplayHelper>())
            .AddSingleton<HeroesProfileProvider>();
}
