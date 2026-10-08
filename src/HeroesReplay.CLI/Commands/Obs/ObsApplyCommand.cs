using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Commands.Obs;

/// <summary>
/// <c>obs apply</c> (#307): merges the install's template changes into the live scene collection,
/// keeping the operator's work. Without <c>--backup</c> it writes nothing.
/// </summary>
public static class ObsApplyCommand
{
    public static Command Create()
    {
        var command = new Command(
            "apply",
            "Merge the install's obs/Default.json changes into the live OBS scene collection and keep the operator's overrides, additions, and removals. Three-way, with the template the collection was last written from (the SHA-256 in managed-collections.json: this install's template, --previous's, or the copy in %LOCALAPPDATA%\\HeroesReplay\\obs\\templates), as `obs plan` shows it. Without --backup it writes nothing and shows the merge, also while OBS runs. With --backup, OBS must be closed (refused while it runs, obs.apply_obs_running): the live collection is backed up to %LOCALAPPDATA%\\HeroesReplay\\obs\\backups, the merge is written atomically, and managed-collections.json names this template (and that the collection keeps the operator's work, so no update replaces it); `obs restore <backup>` undoes it, record included. Refused on a conflict (obs.apply_conflict), without a known base (obs.apply_base_unknown), while a release rollback waits (obs.apply_rollback_pending), and when the merge does not compare as the template plus the operator's work (obs.apply_unverified). Exit 0 when merged, in sync, or ready; 1 when refused or the collection or template cannot be read."
        );
        Option<bool> backup = new("--backup")
        {
            Description =
                "Back up the live collection and write the merge. OBS must be closed. Without it nothing is written.",
        };
        Option<string> install = new("--install")
        {
            Description =
                "The install whose obs/Default.json and settings to merge in. Default: this exe's folder.",
        };
        Option<string> previous = new("--previous")
        {
            Description =
                "The install that wrote the live collection, when --install is a newer one: its template is the base.",
        };
        Option<string> environment = new("--environment")
        {
            Description =
                "appsettings overlay to read (HEROES_REPLAY_ENV). Default: the HEROES_REPLAY_ENV variable.",
        };
        var format = new Option<string>("--output")
        {
            Description =
                "text (default) or json: schemaVersion, ok, code, message, base, written, backup, taken, kept, differences, blocking.",
            DefaultValueFactory = _ => "text",
        };
        format.AcceptOnlyFromAmong("text", "json");
        format.Aliases.Add("-o");
        command.Options.Add(backup);
        command.Options.Add(install);
        command.Options.Add(previous);
        command.Options.Add(environment);
        command.Options.Add(format);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(
                    Run(
                        parseResult.GetValue(backup),
                        parseResult.GetValue(install),
                        parseResult.GetValue(previous),
                        parseResult.GetValue(environment),
                        string.Equals(
                            parseResult.GetValue(format),
                            "json",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                );
            }
        );
        return command;
    }

    private static int Run(
        bool write,
        string install,
        string previous,
        string environment,
        bool json
    )
    {
        string directory = string.IsNullOrWhiteSpace(install)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(install);
        OBSSettings obs;
        string dataDirectory;
        try
        {
            (obs, dataDirectory) = ServiceCollectionExtensions.LoadInstallObsSettings(
                directory,
                string.IsNullOrWhiteSpace(environment)
                    ? Environment.GetEnvironmentVariable("HEROES_REPLAY_ENV")
                    : environment
            );
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"The settings in {directory} could not be read. {e.Message}");
            return 1;
        }

        ObsApplyResult result = ObsCollectionApply.Run(
            new ObsApplyRequest
            {
                TemplatePath = ObsCollectionPaths.FindCollection(directory),
                PreviousTemplatePath = string.IsNullOrWhiteSpace(previous)
                    ? null
                    : ObsCollectionPaths.FindCollection(Path.GetFullPath(previous)),
                CollectionPath = ObsNames.CollectionFile(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    ObsNames.SceneCollection(obs)
                ),
                DataDirectory = dataDirectory,
                Managed = ObsManagedFiles.ForThisUser(),
                ObsIsRunning = NamedProcess.IsRunning(ObsLaunchDecision.ProcessName),
                Write = write,
                Runtime = ObsRuntimeValues.From(obs),
            }
        );
        if (json)
        {
            Console.WriteLine(result.ToJson());
        }
        else
        {
            WriteText(result, result.Ok ? Console.Out : Console.Error);
        }

        return result.Ok ? 0 : 1;
    }

    private static void WriteText(ObsApplyResult result, TextWriter output)
    {
        output.WriteLine($"OBS apply: {(result.Ok ? "ok" : "not ok")} [{result.Code}]");
        output.WriteLine($"  Collection: {result.Collection}");
        output.WriteLine($"  Template:   {result.Template} ({Short(result.TemplateSha256)})");
        output.WriteLine(
            $"  Base:       {result.Base} (recorded {Short(result.RecordedTemplateSha256)}){(result.ObsRunning ? "; OBS is running" : "")}"
        );
        output.WriteLine($"  {result.Message}");
        if (result.Blocking.Count > 0)
        {
            output.WriteLine($"  Refused by ({result.Blocking.Count}):");
            foreach (ObsCollectionDifference difference in result.Blocking)
            {
                output.WriteLine($"    {difference.Describe()}");
            }

            return;
        }

        foreach (
            IGrouping<ObsDiffKind, ObsCollectionDifference> group in result.Differences.GroupBy(
                difference => difference.Kind
            )
        )
        {
            output.WriteLine(
                $"  {ObsCollectionPlanText.Heading(group.Key)} ({group.Count()}), {ObsCollectionPlanText.Apply(group.First().Apply)}:"
            );
            foreach (ObsCollectionDifference difference in group)
            {
                output.WriteLine($"    {difference.Describe()}");
            }
        }
    }

    private static string Short(string hash) =>
        string.IsNullOrEmpty(hash) ? "none" : hash.Substring(0, Math.Min(12, hash.Length));
}
