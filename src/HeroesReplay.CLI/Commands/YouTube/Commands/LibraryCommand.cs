using System;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.Services.Processes;
using HeroesReplay.Core.Services.YouTube;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.YouTube.Commands;

public class LibraryCommand : Command
{
    public LibraryCommand()
        : base(
            "library",
            "File uploaded matches into map playlists. Polls until stopped. Not part of services start. Dry-run does not call YouTube."
        )
    {
        var onceOption = new Option<bool>("--once")
        {
            Description = "File one pass and exit instead of polling.",
            DefaultValueFactory = _ => false,
        };
        Options.Add(onceOption);
        SetAction(
            (parseResult, cancellationToken) =>
                CommandAsync(parseResult.GetValue(onceOption), cancellationToken)
        );
    }

    private static async Task<int> CommandAsync(bool once, CancellationToken cancellationToken)
    {
        using ServiceStopLink stop = ServiceStopFile.Link(cancellationToken);
        using ServiceProvider provider = new ServiceCollection()
            .AddYouTubeServices(stop.Token)
            .BuildHeroesReplayProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        using Activity ready = HeroesReplayTelemetry.StartSpan("heroesreplay.service.ready");
        using IServiceScope scope = provider.CreateScope();
        IYouTubeLibrary library = scope.ServiceProvider.GetRequiredService<IYouTubeLibrary>();
        try
        {
            while (!stop.Token.IsCancellationRequested)
            {
                int code = await library.RunOnceAsync(stop.Token).ConfigureAwait(false);
                if (once)
                {
                    return code;
                }

                await Task.Delay(TimeSpan.FromSeconds(60), stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return 0;
        }

        return 0;
    }
}
