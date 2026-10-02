using System;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Configuration;

namespace HeroesReplay.CLI.Commands.Update;

public class UpdateCommand : Command
{
    public UpdateCommand()
        : base(
            "update",
            "Compare this install with the latest GitHub Release. A source build does not replace itself."
        )
    {
        Subcommands.Add(CheckCommand());
        Subcommands.Add(PreserveMinReplayIdCommand());
        Subcommands.Add(ReleaseHealthCommand());
        Subcommands.Add(MigrateStreamArmCommand());
        Subcommands.Add(InstallObsCommand());
    }

    private static Option<string> EnvironmentOption() =>
        new("--environment")
        {
            Description =
                "appsettings overlay to read (HEROES_REPLAY_ENV). apply-release.ps1 passes the running environment, prod when unset.",
        };

    private static Command MigrateStreamArmCommand()
    {
        var command = new Command(
            "migrate-stream-arm",
            "Called by apply-release.ps1 once: arm this machine for Twitch ingest when the install being replaced has OBS:StreamingEnabled true. Never arms when it is false, and never runs twice."
        );
        Option<string> previous = new("--previous")
        {
            Description = "The install being replaced. Its effective settings are read.",
            Required = true,
        };
        Option<string> environment = EnvironmentOption();
        command.Options.Add(previous);
        command.Options.Add(environment);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(
                    MigrateStreamArm(
                        parseResult.GetValue(previous),
                        parseResult.GetValue(environment)
                    )
                );
            }
        );
        return command;
    }

    private static int MigrateStreamArm(string previous, string environment)
    {
        bool streamingEnabled;
        try
        {
            streamingEnabled = ServiceCollectionExtensions
                .BuildConfiguration(Path.GetFullPath(previous), environment)
                .GetValue<bool>("OBS:StreamingEnabled");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(
                $"Stream arm migration did not run. The settings in {previous} could not be read. {e.Message}"
            );
            return 1;
        }

        try
        {
            StreamArmMigrationResult result = StreamArmMigration.Run(
                new ObsStreamArm(),
                streamingEnabled,
                previous
            );
            Console.WriteLine(result.Message);
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Stream arm migration failed. {e.Message}");
            return 1;
        }
    }

    private static Command InstallObsCommand()
    {
        var command = new Command(
            "install-obs",
            "Called by apply-release.ps1: while OBS is closed, copy the release scene collection and install the profile template only when this machine has no profile. Never copies service.json."
        );
        Option<string> install = new("--install")
        {
            Description = "The install directory that holds obs\\Default.json and appsettings.",
            Required = true,
        };
        Option<string> environment = EnvironmentOption();
        command.Options.Add(install);
        command.Options.Add(environment);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(
                    InstallObs(parseResult.GetValue(install), parseResult.GetValue(environment))
                );
            }
        );
        return command;
    }

    private static int InstallObs(string install, string environment)
    {
        try
        {
            OBSSettings obs =
                ServiceCollectionExtensions
                    .BuildConfiguration(Path.GetFullPath(install), environment)
                    .GetSection("OBS")
                    .Get<OBSSettings>()
                ?? new OBSSettings();
            foreach (
                string note in ReleaseInstall.CopyObsScenesIfClosed(
                    install,
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    NamedProcess.IsRunning(ObsLaunchDecision.ProcessName),
                    ObsNames.Profile(obs),
                    ObsNames.SceneCollection(obs)
                )
            )
            {
                Console.WriteLine(note);
            }

            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"OBS scene files were not copied. {e.Message}");
            return 1;
        }
    }

    private static Command ReleaseHealthCommand()
    {
        var command = new Command(
            "release-health",
            "Called by apply-release.ps1 after it installs a release. Exit 0 once every role in services.json runs with a fresh heartbeat from a process started after --since, and spectate showed match progress (the match clock or the award screen) after it. Exit 1 while Release:HealthWindow is still open (without --wait), 2 when it closed first (roll back), 3 when a stop was requested (no verdict)."
        );
        Option<string> since = new("--since")
        {
            Description =
                "When the new stack was started, UTC in round-trip format. Roles and work from before it do not count.",
            Required = true,
        };
        Option<string> install = new("--install")
        {
            Description =
                "The install whose settings give Release:HealthWindow and ServiceHealth. Default: this exe's folder.",
        };
        Option<string> environment = EnvironmentOption();
        Option<string> window = new("--window")
        {
            Description = "Overrides Release:HealthWindow (hh:mm:ss).",
        };
        Option<bool> wait = new("--wait")
        {
            Description =
                "Check every 15 seconds until the install is healthy, the window closes, or a stop is requested.",
        };
        command.Options.Add(since);
        command.Options.Add(install);
        command.Options.Add(environment);
        command.Options.Add(window);
        command.Options.Add(wait);
        command.SetAction(
            (parseResult, cancellationToken) =>
                Task.FromResult(
                    ReleaseHealthExit(
                        parseResult.GetValue(since),
                        parseResult.GetValue(install),
                        parseResult.GetValue(environment),
                        parseResult.GetValue(window),
                        parseResult.GetValue(wait),
                        cancellationToken
                    )
                )
        );
        return command;
    }

    private static int ReleaseHealthExit(
        string sinceText,
        string install,
        string environment,
        string windowText,
        bool wait,
        CancellationToken cancellationToken
    )
    {
        if (
            !DateTimeOffset.TryParse(
                sinceText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal,
                out DateTimeOffset started
            )
        )
        {
            Console.Error.WriteLine($"--since '{sinceText}' is not a time. Use the o format.");
            return (int)ReleaseHealthVerdict.Unhealthy;
        }

        TimeSpan? overrideWindow = null;
        if (!string.IsNullOrWhiteSpace(windowText))
        {
            if (!TimeSpan.TryParse(windowText, CultureInfo.InvariantCulture, out TimeSpan parsed))
            {
                Console.Error.WriteLine($"--window '{windowText}' is not a time span (hh:mm:ss).");
                return (int)ReleaseHealthVerdict.Unhealthy;
            }

            overrideWindow = parsed;
        }

        IConfiguration settings = InstallSettings(install, environment);
        TimeSpan healthWindow = ReleaseHealth.Window(
            overrideWindow
                ?? settings?.GetSection("Release").Get<ReleaseSettings>()?.HealthWindow
                ?? ReleaseHealth.DefaultWindow
        );
        var check = new ReleaseHealthCheck
        {
            Health =
                settings?.GetSection("ServiceHealth").Get<ServiceHealthSettings>()
                ?? new ServiceHealthSettings(),
        };
        ReleaseHealthResult result = wait
            ? check.WaitForVerdict(started, healthWindow, cancellationToken)
            : check.Check(started, healthWindow);
        if (!wait)
        {
            Console.WriteLine("Release health " + result.Describe());
        }

        return result.ExitCode;
    }

    // A missing or unreadable appsettings.json leaves the defaults.
    private static IConfiguration InstallSettings(string install, string environment)
    {
        string directory = string.IsNullOrWhiteSpace(install)
            ? Path.GetDirectoryName(Environment.ProcessPath)
            : Path.GetFullPath(install);
        if (
            string.IsNullOrWhiteSpace(directory)
            || !File.Exists(Path.Combine(directory, "appsettings.json"))
        )
        {
            return null;
        }

        try
        {
            return ServiceCollectionExtensions.BuildConfiguration(directory, environment);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException)
        {
            Console.Error.WriteLine(
                $"Release health uses default settings. {directory} could not be read: {e.Message}"
            );
            return null;
        }
    }

    private static Command PreserveMinReplayIdCommand()
    {
        var command = new Command(
            "preserve-min-replay-id",
            "Keep the higher MinReplayId when a release replaces appsettings.json."
        );
        Option<string> previous = new("--previous")
        {
            Description = "appsettings.json from the install being replaced.",
            Required = true,
        };
        Option<string> target = new("--target")
        {
            Description = "appsettings.json that the release will install.",
            Required = true,
        };
        command.Options.Add(previous);
        command.Options.Add(target);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(
                    PreserveMinReplayId(
                        parseResult.GetValue(previous),
                        parseResult.GetValue(target)
                    )
                );
            }
        );
        return command;
    }

    private static int PreserveMinReplayId(string previous, string target)
    {
        try
        {
            ReleaseInstall.PreserveMinReplayId(previous, target);
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not preserve MinReplayId. {e.Message}");
            return 1;
        }
    }

    private static Command CheckCommand()
    {
        var command = new Command(
            "check",
            "Print the installed version and the latest release tag. Does not download or restart."
        );
        command.SetAction((parseResult, cancellationToken) => CheckAsync(cancellationToken));
        return command;
    }

    private static async Task<int> CheckAsync(CancellationToken cancellationToken)
    {
        string install = Path.GetDirectoryName(Environment.ProcessPath);
        string local = ReleaseInstall.ReadVersion(install);
        Console.WriteLine(
            string.IsNullOrWhiteSpace(local)
                ? "Installed version: (none)"
                : $"Installed version: {local}"
        );
        if (ReleaseInstall.LooksLikeSourceBuild(install))
        {
            Console.WriteLine("This is a source build. It will not self-update.");
        }

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("HeroesReplay", "1")
            );
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
            );
            string json = await http.GetStringAsync(
                    $"https://api.github.com/repos/{ReleaseSettings.DefaultRepository}/releases/latest",
                    cancellationToken
                )
                .ConfigureAwait(false);
            ReleaseOffer? offer = GitHubReleaseJson.Read(
                json,
                ReleaseSettings.DefaultAssetName,
                localVersion: "\0"
            );
            if (offer is ReleaseOffer found)
            {
                Console.WriteLine($"Latest release: {found.Version}");
                Console.WriteLine(
                    string.Equals(found.Version, local, StringComparison.OrdinalIgnoreCase)
                        ? "This install is current."
                        : "A newer release is available."
                );
                return 0;
            }

            Console.WriteLine("No release asset named heroesreplay-win-x64.zip was found.");
            return 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Could not read the latest release. {e.Message}");
            return 1;
        }
    }
}
