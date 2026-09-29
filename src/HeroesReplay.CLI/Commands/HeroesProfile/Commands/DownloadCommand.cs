using System;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.Core;
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
        while (!stop.Token.IsCancellationRequested)
        {
            bool downloaded = false;
            try
            {
                downloaded = await downloader.DownloadNextAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.Token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Heroes Profile download failed. The downloader stays up.");
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
