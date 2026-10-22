using System;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands.Storage;

/// <summary>
/// <c>heroesreplay storage</c>: what the automation keeps on disk, and the housekeeping pass that
/// retires it (#487).
/// </summary>
public class StorageCommand : Command
{
    public StorageCommand()
        : base(
            "storage",
            "What HeroesReplay keeps under Location:DataDirectory and %LOCALAPPDATA%\\HeroesReplay, and the housekeeping pass that removes what is no longer needed."
        )
    {
        Subcommands.Add(ReportCommand());
    }

    private static Command ReportCommand()
    {
        var command = new Command(
            "report",
            "Show every storage category (files, size, oldest item) and what each pruner's next housekeeping pass would remove, and why. Read only. With --apply, run the pass now (as the supervisor does every Housekeeping:Interval) and report what it removed; Housekeeping:DryRun makes it log only."
        );
        Option<bool> apply = new("--apply")
        {
            Description =
                "Run the housekeeping pass now, then report. Writes housekeeping.json. Honours Housekeeping:DryRun and each Housekeeping:Pruners switch.",
        };
        Option<bool> items = new("--items")
        {
            Description = "List every file and folder the next pass would remove, with its reason.",
        };
        command.Options.Add(apply);
        command.Options.Add(items);
        Option<string> output = CliOutput.CreateOption(
            "JSON: schemaVersion, ok, code (storage.report, storage.applied, storage.dry_run, storage.apply_failed), message, environment, details (dataDirectory, stateDirectory, dataBytes, stateBytes, freeBytes, dryRun, scheduled, intervalSeconds, categories[] (name, description, path, files, bytes, oldest, plannedFiles, plannedBytes), pruners[] (name, description, enabled, count, bytes, warnings, items[] (path, bytes, lastWriteUtc, reason, isDirectory)), lastRun, applied). Pass logs go to stderr."
        );
        command.Options.Add(output);
        command.SetAction(
            (parseResult, _) =>
            {
                CliOutputFormat format = CliOutput.Format(parseResult, output);
                return Task.FromResult(
                    Report(
                        parseResult.GetValue(apply),
                        parseResult.GetValue(items),
                        format,
                        CliOutput.Out(parseResult)
                    )
                );
            }
        );
        return command;
    }

    private static int Report(bool apply, bool items, CliOutputFormat format, TextWriter output)
    {
        AppSettings settings = ServiceCollectionExtensions.LoadOfflineSettings();
        string statePath = HousekeepingFile.PathIn(Housekeeping.StateDirectory(settings));
        HousekeepingRun lastRun = HousekeepingFile.TryLoad(statePath)?.LastRun;
        HousekeepingRun applied = null;
        // The pass logs to stderr, so stdout carries only the report.
        using ILoggerFactory logs = LoggerFactory.Create(builder =>
            builder
                .SetMinimumLevel(LogLevel.Information)
                .AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace)
        );
        ILogger logger = logs.CreateLogger<HousekeepingPass>();
        if (apply)
        {
            applied = Housekeeping
                .RunAndRecord(settings, DateTimeOffset.UtcNow, Housekeeping.ReportTrigger, logger)
                .LastRun;
        }

        StorageReportDetails details = StorageReport.Build(
            settings,
            Housekeeping.CreatePass(settings, logger),
            DateTimeOffset.UtcNow,
            lastRun,
            applied
        );
        CliResult<StorageReportDetails> result = StorageReport.Result(details);
        if (format == CliOutputFormat.Json)
        {
            return CliOutput.WriteJson(result, output);
        }

        WriteText(details, items, output);
        output.WriteLine(result.Message);
        return CliOutput.ExitCode(result);
    }

    public static void WriteText(StorageReportDetails details, bool items, TextWriter output)
    {
        output.WriteLine(
            $"Data {details.DataDirectory}: {Size.Describe(details.DataBytes)}. {details.StateDirectory}: {Size.Describe(details.StateBytes)}. Free: {(details.FreeBytes is long free ? Size.Describe(free) : "unknown")}."
        );
        output.WriteLine(
            details.Scheduled
                ? $"The supervisor runs housekeeping every {TimeSpan.FromSeconds(details.IntervalSeconds):g}{(details.DryRun ? " in dry-run mode (Housekeeping:DryRun): it logs and removes nothing" : string.Empty)}."
                : "Housekeeping:Enabled is false: the supervisor runs no pass."
        );
        output.WriteLine();
        output.WriteLine(
            $"{"Category", -16} {"Files", 7} {"Size", 10}  {"Oldest", -10}  Next pass removes"
        );
        foreach (StorageCategoryUsage row in details.Categories)
        {
            string planned =
                row.PlannedFiles == 0
                    ? "-"
                    : $"{row.PlannedFiles} files, {Size.Describe(row.PlannedBytes)}";
            string oldest = row.Oldest?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
            output.WriteLine(
                $"{row.Name, -16} {row.Files, 7} {Size.Describe(row.Bytes), 10}  {oldest, -10}  {planned}"
            );
        }

        output.WriteLine();
        output.WriteLine("Pruners:");
        foreach (StoragePrunerPlan pruner in details.Pruners)
        {
            string plan =
                !pruner.Enabled ? $"off (Housekeeping:Pruners:{pruner.Name} is false)"
                : pruner.Count == 0 ? "nothing to remove"
                : $"{pruner.Count} items, {Size.Describe(pruner.Bytes)}";
            output.WriteLine($"  {pruner.Name}: {plan}. {pruner.Description}");
            foreach (string warning in pruner.Warnings)
            {
                output.WriteLine($"    warning: {warning}");
            }

            if (!items)
            {
                continue;
            }

            foreach (StoragePlannedItem item in pruner.Items)
            {
                output.WriteLine(
                    $"    {item.Path}{(item.IsDirectory ? "\\" : string.Empty)} ({Size.Describe(item.Bytes)}, {item.LastWriteUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}): {item.Reason}"
                );
            }
        }

        output.WriteLine();
        HousekeepingRun last = details.Applied ?? details.LastRun;
        output.WriteLine(
            last == null
                ? "No housekeeping pass has been recorded yet."
                : $"Last pass {last.StartedAt.ToString("u", CultureInfo.InvariantCulture)} ({last.Trigger}): {last.Describe()}."
        );
        if (!items && details.Pruners.Any(pruner => pruner.Count > 0))
        {
            output.WriteLine("--items lists each file and folder with its reason.");
        }
    }
}
