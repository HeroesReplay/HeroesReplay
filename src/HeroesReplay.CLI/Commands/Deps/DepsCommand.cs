using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Dependencies;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Commands.Deps;

/// <summary>
/// <c>heroesreplay deps</c>: the external tools HeroesReplay runs, pinned in
/// <c>src/HeroesReplay.Core/Dependencies/dependencies.json</c>.
/// </summary>
public class DepsCommand : Command
{
    public const string Installed = "deps.installed";
    public const string AlreadyInstalled = "deps.already_installed";
    public const string Failed = "deps.failed";

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
        Option<string> output = CliOutput.CreateOption(
            "JSON: schemaVersion, ok, code (deps.installed, deps.already_installed, deps.failed), message, environment, details (directory, tools[] with name, version, outcome, ok, message; note). Download progress goes to stderr."
        );
        command.Options.Add(output);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                CliOutputFormat format = CliOutput.Format(parseResult, output);
                TextWriter stdout = CliOutput.Out(parseResult);
                return InstallAsync(
                    parseResult.GetValue(directory),
                    format,
                    stdout,
                    CliOutput.Progress(format, parseResult, stdout),
                    cancellationToken
                );
            }
        );
        return command;
    }

    private static async Task<int> InstallAsync(
        string directory,
        CliOutputFormat format,
        TextWriter output,
        TextWriter progress,
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
        var tools = new List<DepsToolResult>();
        // Dependencies:DownloadTimeout bounds the whole install, so the client has no limit of its own.
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HeroesReplay", "1"));
        var installer = new DependencyInstaller(http, progress.WriteLine);
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

            if (format == CliOutputFormat.Text)
            {
                output.WriteLine(result.Message);
            }

            tools.Add(
                new DepsToolResult(pin.Name, pin.Version, result.Outcome, result.Ok, result.Message)
            );
        }

        string note = null;
        if (!string.IsNullOrWhiteSpace(directory) && tools.All(tool => tool.Ok))
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
                note =
                    $"Clips look in {locator.InstallDirectory}, not {root}. Set Dependencies:Directory (or Clips:FfmpegDirectory) to use this copy.";
                if (format == CliOutputFormat.Text)
                {
                    output.WriteLine(note);
                }
            }
        }

        CliResult<DepsInstallDetails> report = Report(root, tools, note);
        return format == CliOutputFormat.Json
            ? CliOutput.WriteJson(report, output)
            : CliOutput.ExitCode(report);
    }

    /// <summary>
    /// The <c>deps install</c> result: <see cref="Failed"/> when a tool failed (exit 1),
    /// <see cref="Installed"/> when one was downloaded, else <see cref="AlreadyInstalled"/>.
    /// </summary>
    public static CliResult<DepsInstallDetails> Report(
        string directory,
        IReadOnlyList<DepsToolResult> tools,
        string note
    )
    {
        DepsToolResult failed = tools.FirstOrDefault(tool => !tool.Ok);
        bool installed = tools.Any(tool => tool.Outcome == DependencyInstallOutcome.Installed);
        return new CliResult<DepsInstallDetails>
        {
            Ok = failed == null,
            Code =
                failed != null ? Failed
                : installed ? Installed
                : AlreadyInstalled,
            Message = failed?.Message ?? string.Join(" ", tools.Select(tool => tool.Message)),
            Environment = CliJson.CurrentEnvironment(),
            Details = new DepsInstallDetails(directory, tools, note),
        };
    }
}

/// <summary>One pinned tool after <c>deps install</c>.</summary>
public sealed record DepsToolResult(
    string Name,
    string Version,
    DependencyInstallOutcome Outcome,
    bool Ok,
    string Message
);

/// <summary>
/// <c>deps install --output json</c> details. <see cref="Note"/> is set when <c>--dir</c> is not
/// where clips look.
/// </summary>
public sealed record DepsInstallDetails(
    string Directory,
    IReadOnlyList<DepsToolResult> Tools,
    string Note
);
