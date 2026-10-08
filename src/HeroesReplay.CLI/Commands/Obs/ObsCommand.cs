using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.Obs.Pages;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Commands.Obs;

public class ObsCommand : Command
{
    public ObsCommand()
        : base(
            "obs",
            "OBS on this machine: the Twitch ingest arm, the report-scene pages, and read-only inspection and validation. Twitch ingest starts only when OBS:StreamingEnabled is true and this machine is armed."
        )
    {
        Subcommands.Add(ArmCommand());
        Subcommands.Add(DisarmCommand());
        Subcommands.Add(StatusCommand());
        Subcommands.Add(PagesCommand());
        Subcommands.Add(ObsLiveCommands.InspectCommand());
        Subcommands.Add(ObsLiveCommands.ValidateCommand());
    }

    private static Command PagesCommand()
    {
        var command = new Command(
            "pages",
            "Render the report-scene pages in Location:DataDirectory (queue.html, prediction-report.html) with this build, from the saved request queue and the last prediction report, then reload the OBS browser sources that show them. The reload is the only change made in OBS; it is skipped when OBS is not running. Exit 1 only when a page could not be written."
        );
        Option<bool> noReload = new("--no-reload")
        {
            Description =
                "Write the pages only. OBS reloads each one when its scene is next shown.",
        };
        command.Options.Add(noReload);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(Pages(parseResult.GetValue(noReload)));
            }
        );
        return command;
    }

    private static int Pages(bool noReload)
    {
        AppSettings settings;
        IReadOnlyList<ObsPageResult> results;
        try
        {
            settings = ServiceCollectionExtensions.LoadOfflineSettings();
            results = ObsPages.Write(settings);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"The OBS pages were not written. {e.Message}");
            return 1;
        }

        Console.WriteLine($"Data directory: {settings.Location.DataDirectory}");
        foreach (ObsPageResult result in results)
        {
            string line =
                $"{result.FileName}: {result.Outcome.ToString().ToLowerInvariant()}. {result.Detail}";
            if (result.Outcome == ObsPageOutcome.Failed)
            {
                Console.Error.WriteLine(line + " The page was left as it is.");
            }
            else
            {
                Console.WriteLine(line);
            }
        }

        string[] written = results
            .Where(result => result.Outcome == ObsPageOutcome.Written)
            .Select(result => result.FileName)
            .ToArray();
        if (!noReload && written.Length > 0)
        {
            ReloadPages(settings, written);
        }

        return results.Any(result => result.Outcome == ObsPageOutcome.Failed) ? 1 : 0;
    }

    private static void ReloadPages(AppSettings settings, string[] written)
    {
        if (!NamedProcess.IsRunning(ObsLaunchDecision.ProcessName))
        {
            Console.WriteLine("OBS is not running. It loads the new pages when it starts.");
            return;
        }

        try
        {
            using IObsPageSession obs = new ObsWebsocketPageSessionFactory().Open(
                settings.OBS?.WebSocketEndpoint,
                SecretResolver.Resolve(settings.OBS?.WebSocketPassword)
            );
            IReadOnlyList<string> reloaded = ObsPages.Reload(
                obs,
                settings.Location.DataDirectory,
                written
            );
            Console.WriteLine(
                reloaded.Count == 0
                    ? "OBS has no browser source that shows these pages."
                    : $"Reloaded in OBS: {string.Join(", ", reloaded)}."
            );
        }
        catch (Exception e)
        {
            // The pages are written; OBS still reloads each one when its scene is shown.
            Console.Error.WriteLine(
                $"Warning: OBS did not reload the pages. {e.Message} Each page reloads when its scene is next shown."
            );
        }
    }

    private static Command ArmCommand()
    {
        var command = new Command(
            "arm",
            "Allow this machine to start Twitch ingest. Writes %LOCALAPPDATA%\\HeroesReplay\\stream-armed. Run it only on the stream PC; the spectator still needs OBS:StreamingEnabled (prod)."
        );
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(Arm(new ObsStreamArm()));
            }
        );
        return command;
    }

    private static Command DisarmCommand()
    {
        var command = new Command(
            "disarm",
            "Stop this machine from starting Twitch ingest. Deletes the arm file. A stream that is already live keeps running until services stop or OBS stops it."
        );
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(Disarm(new ObsStreamArm()));
            }
        );
        return command;
    }

    private static Command StatusCommand()
    {
        var command = new Command(
            "status",
            "Print the stream arm, OBS:StreamingEnabled, and the expected OBS profile and scene collection. Does not connect to OBS."
        );
        Option<string> output = CliOutput.CreateOption(
            "JSON: schemaVersion, ok, code (obs.ingest_ready, obs.stream_not_armed, obs.streaming_disabled, obs.settings_unreadable), message, environment, details (streamArm, profile, sceneCollection)."
        );
        command.Options.Add(output);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var arm = new ObsStreamArm();
                CliResult<ObsStatusDetails> status = ReadStatus(
                    arm.IsArmed(),
                    arm.FilePath,
                    ServiceCollectionExtensions.LoadObsSettings
                );
                TextWriter stdout = CliOutput.Out(parseResult);
                return Task.FromResult(
                    CliOutput.Format(parseResult, output) == CliOutputFormat.Json
                        ? CliOutput.WriteJson(status, stdout)
                        : WriteStatus(status, stdout, CliOutput.Error(parseResult))
                );
            }
        );
        return command;
    }

    private static int Arm(ObsStreamArm arm)
    {
        try
        {
            arm.Arm("Armed by heroesreplay obs arm.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not arm this machine. {e.Message}");
            return 1;
        }

        Console.WriteLine($"Armed: {arm.FilePath}");
        Console.WriteLine(
            "The spectator starts Twitch ingest on its next reconcile when OBS:StreamingEnabled is true."
        );
        return 0;
    }

    private static int Disarm(ObsStreamArm arm)
    {
        bool removed;
        try
        {
            removed = arm.Disarm();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not disarm this machine. {e.Message}");
            return 1;
        }

        Console.WriteLine(
            removed ? $"Disarmed: removed {arm.FilePath}" : $"Not armed: {arm.FilePath} is absent."
        );
        Console.WriteLine(
            "No new stream starts. A live stream keeps running until services stop or OBS stops it."
        );
        return 0;
    }

    public const string IngestReady = "obs.ingest_ready";
    public const string StreamingDisabled = "obs.streaming_disabled";

    /// <summary>
    /// <c>obs status</c> without connecting to OBS. Ok unless the settings cannot be loaded
    /// (<see cref="ObsLiveRead.SettingsUnreadable"/>, exit 1). The code says whether this
    /// machine may start Twitch ingest: <see cref="IngestReady"/>,
    /// <see cref="ObsStreamArm.NotArmedReason"/>, or <see cref="StreamingDisabled"/>.
    /// </summary>
    public static CliResult<ObsStatusDetails> ReadStatus(
        bool armed,
        string armFile,
        Func<OBSSettings> load
    )
    {
        OBSSettings obs;
        try
        {
            obs = load();
        }
        catch (Exception e)
        {
            return new CliResult<ObsStatusDetails>
            {
                Ok = false,
                Code = ObsLiveRead.SettingsUnreadable,
                Message = $"Settings could not be loaded. {e.Message}",
                Environment = CliJson.CurrentEnvironment(),
                Details = new ObsStatusDetails(
                    new ObsStreamArmInfo(armed, armFile, false, false, null),
                    null,
                    null
                ),
            };
        }

        bool streaming = SessionMedia.ShouldStream(obs);
        string blocked = TwitchIngestGuard.BlockedBy(streaming, armed);
        bool mayStart = streaming && armed;
        return new CliResult<ObsStatusDetails>
        {
            Ok = true,
            Code = mayStart ? IngestReady : blocked ?? StreamingDisabled,
            Message = mayStart
                ? "Twitch ingest: may start (the profile and scene collection are checked first)."
                : "Twitch ingest: off. "
                    + (blocked != null ? blocked + ". " : string.Empty)
                    + TwitchIngestGuard.Refusal(streaming, armed),
            Environment = CliJson.CurrentEnvironment(),
            Details = new ObsStatusDetails(
                new ObsStreamArmInfo(armed, armFile, streaming, mayStart, blocked),
                ObsNames.Profile(obs),
                ObsNames.SceneCollection(obs)
            ),
        };
    }

    private static int WriteStatus(
        CliResult<ObsStatusDetails> status,
        TextWriter output,
        TextWriter error
    )
    {
        ObsStreamArmInfo arm = status.Details.StreamArm;
        output.WriteLine($"Stream arm: {(arm.Armed ? "armed" : "not armed")} ({arm.ArmFile})");
        if (!status.Ok)
        {
            error.WriteLine(status.Message);
            return 1;
        }

        output.WriteLine($"OBS:StreamingEnabled: {arm.StreamingEnabled}");
        output.WriteLine($"OBS profile: {status.Details.Profile} (OBS:ProfileName)");
        output.WriteLine(
            $"OBS scene collection: {status.Details.SceneCollection} (OBS:SceneCollectionName)"
        );
        output.WriteLine(status.Message);
        return 0;
    }
}

/// <summary><c>obs status --output json</c> details: the arm, and the profile and collection HeroesReplay expects.</summary>
public sealed record ObsStatusDetails(
    ObsStreamArmInfo StreamArm,
    string Profile,
    string SceneCollection
);
