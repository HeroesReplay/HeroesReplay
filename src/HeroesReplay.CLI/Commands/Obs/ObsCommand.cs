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
using HeroesReplay.Core.Status;

namespace HeroesReplay.CLI.Commands.Obs;

public class ObsCommand : Command
{
    public ObsCommand()
        : base(
            "obs",
            "OBS on this machine: the Twitch ingest arm, the report-scene pages, read-only inspection and validation, the check of the install's OBS files against their manifest, the plan of what an update would change in the scene collection, a merge of template changes that keeps the operator's work, and the collection's backups. Twitch ingest starts only when OBS:StreamingEnabled is true and this machine is armed."
        )
    {
        Subcommands.Add(ArmCommand());
        Subcommands.Add(DisarmCommand());
        Subcommands.Add(StatusCommand());
        Subcommands.Add(PagesCommand());
        Subcommands.Add(ObsLiveCommands.InspectCommand());
        Subcommands.Add(ObsLiveCommands.ValidateCommand());
        Subcommands.Add(ObsBundleCommand.Create());
        Subcommands.Add(ObsPlanCommand.Create());
        Subcommands.Add(ObsApplyCommand.Create());
        Subcommands.Add(ObsBackupCommands.BackupCommand());
        Subcommands.Add(ObsBackupCommands.RestoreCommand());
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
            "Print the stream arm, OBS:StreamingEnabled, the expected OBS profile and scene collection, and the stream state spectate last wrote to status.json (Live, Reconnecting, Stalled, Inactive, Unknown). Does not connect to OBS."
        );
        Option<string> output = CliOutput.CreateOption(
            "JSON: schemaVersion, ok, code (obs.ingest_ready, obs.stream_not_armed, obs.streaming_disabled, obs.settings_unreadable), message, environment, details (streamArm, profile, sceneCollection, stream: the state, active, reconnecting, stuckSince and updatedAt from status.json, null without one)."
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
                    ServiceCollectionExtensions.LoadObsSettings,
                    () => new SpectatorStatusStore().TryReadShared()
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
        Func<OBSSettings> load,
        Func<SpectatorStatus> spectator = null
    )
    {
        ObsStatusStream stream = ObsStatusStream.From(ReadSpectator(spectator));
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
                    null,
                    stream
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
                ObsNames.SceneCollection(obs),
                stream
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
        output.WriteLine(ObsStatusStream.Describe(status.Details.Stream));
        return 0;
    }

    private static SpectatorStatus ReadSpectator(Func<SpectatorStatus> spectator)
    {
        try
        {
            return spectator?.Invoke();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// <c>obs status --output json</c> details: the arm, the profile and collection HeroesReplay
/// expects, and the stream state spectate last wrote to status.json (null without one).
/// </summary>
public sealed record ObsStatusDetails(
    ObsStreamArmInfo StreamArm,
    string Profile,
    string SceneCollection,
    ObsStatusStream Stream = null
);

/// <summary>
/// The stream state from status.json (#395), as spectate last read it from OBS: <c>Live</c>,
/// <c>Reconnecting</c>, <c>Stalled</c>, <c>Inactive</c>, or <c>Unknown</c>. <c>obs status</c>
/// does not connect to OBS, so this is the spectator's view, with when it was written.
/// </summary>
public sealed record ObsStatusStream(
    string State,
    bool? Active,
    bool? Reconnecting,
    DateTimeOffset? StuckSince,
    DateTimeOffset UpdatedAt,
    bool Stale
)
{
    public static ObsStatusStream From(SpectatorStatus status) =>
        status?.ObsStreamDesired == null
            ? null
            : new ObsStatusStream(
                status.ObsStreamState,
                status.ObsStreamActive,
                status.ObsStreamReconnecting,
                status.ObsStreamStuckSince,
                status.UpdatedAt,
                status.SnapshotStale
            );

    public static string Describe(ObsStatusStream stream)
    {
        if (stream == null)
        {
            return "Stream (status.json): no spectator status.";
        }

        string since = stream.StuckSince is DateTimeOffset stuck
            ? $" since {stuck.ToUniversalTime():yyyy-MM-dd HH:mm:ss}Z"
            : string.Empty;
        string stale = stream.Stale ? ", stale" : string.Empty;
        return $"Stream (status.json, written {stream.UpdatedAt.ToUniversalTime():yyyy-MM-dd HH:mm:ss}Z{stale}): {stream.State ?? "not read"}{since}.";
    }
}
