using System;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.YouTube.Playlists;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.YouTube.Commands;

public class LibraryCommand : Command
{
    public LibraryCommand()
        : base(
            "library",
            "Run the YouTube library pass now: list the channel's uploads, record videos missing from Data\\youtube-library.jsonl (Heroes Profile fills a missing map, mode, rank, or build), and file them into map and patch playlists. The uploader that services start launches runs the same pass at most every YouTube:LibraryInterval. Both share the daily quota units in Data\\youtube-quota-units.json, and only one process runs the pass at a time. Without --once this repeats the pass each LibraryInterval until stopped. Dry-run writes Data\\youtube-library-dry-run.json and does not call YouTube."
        )
    {
        var onceOption = new Option<bool>("--once")
        {
            Description = "Run one pass and exit.",
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
        bool force = true;
        try
        {
            while (!stop.Token.IsCancellationRequested)
            {
                YouTubeLibraryPass pass;
                try
                {
                    pass = await library.RunOnceAsync(force, stop.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Console.Error.WriteLine("YouTube library pass failed: " + e.Message);
                    if (once)
                    {
                        return 1;
                    }

                    pass = null;
                }

                if (pass != null && (force || pass.Skipped == null))
                {
                    Print(pass);
                }

                if (once)
                {
                    return 0;
                }

                force = false;
                await Task.Delay(TimeSpan.FromSeconds(60), stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return 0;
        }

        return 0;
    }

    private static void Print(YouTubeLibraryPass pass)
    {
        if (pass.Skipped != null)
        {
            Console.WriteLine("YouTube library pass skipped: " + pass.Skipped + ".");
            return;
        }

        Console.WriteLine(
            pass.DryRun
                ? $"YouTube library dry-run: {pass.Planned.Count} playlist insert(s) planned, {pass.Unresolved} unresolved. YouTube was not called."
                : $"YouTube library pass: {pass.NewVideos} new channel video(s), {pass.Recorded} recorded, {pass.Unresolved} unresolved, {pass.Filed} of {pass.Planned.Count} playlist insert(s) filed, {pass.UnitsSpent} units."
        );
        foreach (YouTubeLibraryItem item in pass.Planned)
        {
            Console.WriteLine($"  {item.PlaylistTitle} <- {item.VideoId} (replay {item.ReplayId})");
        }
    }
}
