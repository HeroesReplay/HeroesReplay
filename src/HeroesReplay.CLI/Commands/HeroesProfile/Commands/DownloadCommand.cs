using System;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.Core;
using HeroesReplay.Core.Services.Connectivity;
using HeroesReplay.Core.Services.Processes;
using HeroesReplay.Core.Services.Providers;
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
        using ServiceProvider provider = new ServiceCollection()
            .AddTwitchServices(stop.Token, "heroesreplay-download")
            .AddSingleton<ReplayLoader>()
            .AddSingleton<IReplayLoader>(sp => sp.GetRequiredService<ReplayLoader>())
            .AddSingleton<ReplayHelper>()
            .AddSingleton<IReplayHelper>(sp => sp.GetRequiredService<ReplayHelper>())
            .AddSingleton<HeroesProfileProvider>()
            .BuildHeroesReplayProvider();
        using Activity ready = HeroesReplayTelemetry.StartSpan("heroesreplay.service.ready");
        using IServiceScope scope = provider.CreateScope();
        HeroesProfileProvider downloader =
            scope.ServiceProvider.GetRequiredService<HeroesProfileProvider>();
        ILogger<DownloadCommand> logger = scope.ServiceProvider.GetRequiredService<
            ILogger<DownloadCommand>
        >();
        ServiceReadyFile.ReportFromEnvironment("download");
        ServiceReadyFile.ReportHeartbeatFromEnvironment();
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
}
