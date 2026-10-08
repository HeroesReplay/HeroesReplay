using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Commands.Obs;

/// <summary>
/// <c>obs plan</c> (#307): what an update would change in the live scene collection. Files only,
/// so it is safe while OBS runs; it writes nothing.
/// </summary>
public static class ObsPlanCommand
{
    public static Command Create()
    {
        var command = new Command(
            "plan",
            "Show what an update would change in the live OBS scene collection, without changing anything. Reads files only (no websocket), so it is safe while OBS runs. It compares the live collection with the install's obs/Default.json, after the path rewrite, property by property for every source, filter, and scene item, and with the template the collection was last written from: each difference is a managed change, managed addition or removal (the template moved), an operator override, addition, or removal (kept), a conflict (both changed), or unattributed (no base). It also says what `update install-obs` would do now (create, replace, update paths, restore a waiting rollback, or keep a custom collection), and whether that waits for OBS. Exit 0 when nothing conflicts, 1 on a conflict or when the collection or template cannot be read."
        );
        Option<string> install = new("--install")
        {
            Description =
                "The install whose obs/Default.json and settings an update would use, such as a staged release. Default: this exe's folder.",
        };
        Option<string> previous = new("--previous")
        {
            Description =
                "The install running now, when --install is a newer one: its template is the base when the live collection was written from it.",
        };
        Option<string> environment = new("--environment")
        {
            Description =
                "appsettings overlay to read (HEROES_REPLAY_ENV). Default: the HEROES_REPLAY_ENV variable.",
        };
        Option<string> format = CliOutput.CreateOption(
            "JSON: schemaVersion, ok, code, message, collection, template, base, update, summary, differences."
        );
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
                        parseResult.GetValue(install),
                        parseResult.GetValue(previous),
                        parseResult.GetValue(environment),
                        CliOutput.Format(parseResult, format),
                        CliOutput.Out(parseResult),
                        CliOutput.Error(parseResult)
                    )
                );
            }
        );
        return command;
    }

    private static int Run(
        string install,
        string previous,
        string environment,
        CliOutputFormat format,
        TextWriter output,
        TextWriter error
    )
    {
        ObsCollectionPlanResult plan = Plan(install, previous, environment);
        if (format == CliOutputFormat.Json)
        {
            return CliOutput.WriteJson(plan, output);
        }

        if (plan.Code == ObsLiveRead.SettingsUnreadable)
        {
            error.WriteLine(plan.Message);
        }
        else
        {
            WriteText(plan, output);
        }

        return CliOutput.ExitCode(plan);
    }

    /// <summary>
    /// The plan for <paramref name="install"/>, or <see cref="ObsLiveRead.SettingsUnreadable"/>
    /// when its settings cannot be read.
    /// </summary>
    private static ObsCollectionPlanResult Plan(string install, string previous, string environment)
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
            return new ObsCollectionPlanResult
            {
                Ok = false,
                Code = ObsLiveRead.SettingsUnreadable,
                Message = $"The settings in {directory} could not be read. {e.Message}",
            };
        }

        string template = ObsCollectionPaths.FindCollection(directory);
        ObsManagedFiles managed = ObsManagedFiles.ForThisUser();
        return ObsCollectionPlan.Build(
            new ObsCollectionPlanRequest
            {
                TemplatePath = template,
                // OBS:StableAssets: the copy an update would make and point at (#330).
                AssetRoot =
                    obs.StableAssets && template != null
                        ? ObsAssetStore.For(managed).Planned(Path.GetDirectoryName(template))
                        : null,
                PreviousTemplatePath = string.IsNullOrWhiteSpace(previous)
                    ? null
                    : ObsCollectionPaths.FindCollection(Path.GetFullPath(previous)),
                CollectionPath = ObsNames.CollectionFile(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    ObsNames.SceneCollection(obs)
                ),
                CollectionName = ObsNames.SceneCollection(obs),
                DataDirectory = dataDirectory,
                Managed = managed,
                ObsIsRunning = NamedProcess.IsRunning(ObsLaunchDecision.ProcessName),
                LiveCollectionSwap = obs.LiveCollectionSwap,
                Runtime = ObsRuntimeValues.From(obs),
            }
        );
    }

    public static void WriteText(ObsCollectionPlanResult plan, TextWriter output)
    {
        output.WriteLine($"OBS plan: {(plan.Ok ? "ok" : "not ok")} [{plan.Code}]");
        output.WriteLine($"  Collection: {plan.Collection}");
        output.WriteLine($"  Template:   {plan.Template} ({Short(plan.TemplateSha256)})");
        output.WriteLine(
            $"  Base:       {plan.Base} (recorded {Short(plan.RecordedTemplateSha256)}){(plan.ObsRunning ? "; OBS is running" : "")}"
        );
        output.WriteLine($"  {plan.Message}");
        if (plan.PendingRollback != null)
        {
            output.WriteLine($"  Rollback: {plan.PendingRollback}");
        }

        foreach (
            IGrouping<ObsDiffKind, ObsCollectionDifference> group in plan.Differences.GroupBy(
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

/// <summary>The words <c>obs plan</c> prints for each kind of difference.</summary>
internal static class ObsCollectionPlanText
{
    public static string Heading(ObsDiffKind kind) =>
        kind switch
        {
            ObsDiffKind.ManagedChange => "Managed changes",
            ObsDiffKind.ManagedAddition => "Managed additions",
            ObsDiffKind.ManagedRemoval => "Managed removals",
            ObsDiffKind.OperatorOverride => "Operator overrides",
            ObsDiffKind.OperatorAddition => "Operator additions",
            ObsDiffKind.OperatorRemoval => "Operator removals",
            ObsDiffKind.Conflict => "Conflicts",
            _ => "Unattributed (no base)",
        };

    public static string Apply(ObsDiffApply apply) =>
        apply switch
        {
            ObsDiffApply.Template => "a merge takes the template's",
            ObsDiffApply.Refuse => "a merge refuses",
            _ => "a merge keeps the live one",
        };
}
