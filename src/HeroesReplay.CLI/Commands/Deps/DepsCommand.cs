using System;
using System.CommandLine;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Dependencies;

namespace HeroesReplay.CLI.Commands.Deps;

/// <summary>
/// <c>heroesreplay deps</c>: the external tools HeroesReplay runs, pinned in
/// <c>src/HeroesReplay.Core/Dependencies/dependencies.json</c>.
/// </summary>
public class DepsCommand : Command
{
    public DepsCommand()
        : base(
            "deps",
            "External tools HeroesReplay runs (ffmpeg and ffprobe for clips), pinned with version, URL, and SHA-256 in src/HeroesReplay.Core/Dependencies/dependencies.json."
        )
    {
        Subcommands.Add(InstallCommand());
    }

    private static Command InstallCommand()
    {
        var command = new Command(
            "install",
            "Download the pinned ffmpeg build, check its size and SHA-256, and extract only ffmpeg.exe and ffprobe.exe into <dir>\\ffmpeg. Does nothing when that build is already installed. Safe to rerun: files are staged, then moved into place, so an exe is never half-written. apply-release.ps1 runs it after each install; a failure there only warns. Exit 1 when the download, the hash, or the copy fails."
        );
        Option<string> directory = new("--dir")
        {
            Description =
                "Tools folder; ffmpeg goes in <dir>\\ffmpeg. Default Dependencies:Directory (C:\\heroesreplay\\tools), which FfmpegLocator searches after Clips:FfmpegDirectory.",
        };
        command.Options.Add(directory);
        command.SetAction(
            (parseResult, cancellationToken) =>
                InstallAsync(parseResult.GetValue(directory), cancellationToken)
        );
        return command;
    }

    private static async Task<int> InstallAsync(
        string directory,
        CancellationToken cancellationToken
    )
    {
        (ClipSettings clips, DependencySettings settings) =
            ServiceCollectionExtensions.LoadToolSettings();
        string root = DependencySettings.Root(settings, directory);
        TimeSpan timeout =
            settings.DownloadTimeout > TimeSpan.Zero
                ? settings.DownloadTimeout
                : TimeSpan.FromMinutes(5);
        bool ok = true;
        // Dependencies:DownloadTimeout bounds the whole install, so the client has no limit of its own.
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HeroesReplay", "1"));
        var installer = new DependencyInstaller(http, Console.WriteLine);
        foreach (DependencyPin pin in DependencyManifest.Pins)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(timeout);
            DependencyInstallResult result;
            try
            {
                result = await installer.InstallAsync(pin, root, limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = new(
                    DependencyInstallOutcome.Failed,
                    root,
                    $"{pin.Name} {pin.Version} was not installed: no finish within Dependencies:DownloadTimeout ({timeout})."
                );
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                result = new(
                    DependencyInstallOutcome.Failed,
                    root,
                    $"{pin.Name} {pin.Version} was not installed: {e.Message}"
                );
            }

            Console.WriteLine(result.Message);
            ok &= result.Ok;
        }

        if (!string.IsNullOrWhiteSpace(directory) && ok)
        {
            FfmpegLocator locator = FfmpegLocator.From(clips, settings);
            if (
                !string.Equals(
                    locator.InstallDirectory,
                    Path.Combine(root, DependencyManifest.FfmpegName),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                Console.WriteLine(
                    $"Clips look in {locator.InstallDirectory}, not {root}. Set Dependencies:Directory (or Clips:FfmpegDirectory) to use this copy."
                );
            }
        }

        return ok ? 0 : 1;
    }
}
