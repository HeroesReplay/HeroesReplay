using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Pages;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Commands.Obs;

public class ObsCommand : Command
{
    public ObsCommand()
        : base(
            "obs",
            "OBS on this machine: the Twitch ingest arm, the report-scene pages, read-only inspection and validation, and the check of the install's OBS files against their manifest. Twitch ingest starts only when OBS:StreamingEnabled is true and this machine is armed."
        )
    {
        Subcommands.Add(ArmCommand());
        Subcommands.Add(DisarmCommand());
        Subcommands.Add(StatusCommand());
        Subcommands.Add(PagesCommand());
        Subcommands.Add(ObsLiveCommands.InspectCommand());
        Subcommands.Add(ObsLiveCommands.ValidateCommand());
        Subcommands.Add(ObsBundleCommand.Create());
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
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(Status(new ObsStreamArm()));
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

    private static int Status(ObsStreamArm arm)
    {
        bool armed = arm.IsArmed();
        Console.WriteLine($"Stream arm: {(armed ? "armed" : "not armed")} ({arm.FilePath})");
        OBSSettings obs;
        try
        {
            obs = ServiceCollectionExtensions.LoadObsSettings();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Settings could not be loaded. {e.Message}");
            return 1;
        }

        bool streaming = SessionMedia.ShouldStream(obs);
        Console.WriteLine($"OBS:StreamingEnabled: {streaming}");
        Console.WriteLine($"OBS profile: {ObsNames.Profile(obs)} (OBS:ProfileName)");
        Console.WriteLine(
            $"OBS scene collection: {ObsNames.SceneCollection(obs)} (OBS:SceneCollectionName)"
        );
        string blocked = TwitchIngestGuard.BlockedBy(streaming, armed);
        Console.WriteLine(
            streaming && armed
                ? "Twitch ingest: may start (the profile and scene collection are checked first)."
                : "Twitch ingest: off. "
                    + (blocked != null ? blocked + ". " : string.Empty)
                    + TwitchIngestGuard.Refusal(streaming, armed)
        );
        return 0;
    }
}
