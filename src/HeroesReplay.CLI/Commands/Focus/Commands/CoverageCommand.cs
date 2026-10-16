using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Playlists;

namespace HeroesReplay.CLI.Commands.Focus.Commands;

/// <summary>
/// What the focus picks have covered (#433): every hero of the roster, covered or not, and the
/// spread over roles, maps, leagues, regions and builds, from the ledger and the YouTube library.
/// </summary>
public class CoverageCommand : Command
{
    public const string Read = "focus.coverage";

    public CoverageCommand()
        : base(
            "coverage",
            "Report what the focus picks have covered: picks, plays and uploads; heroes covered out of the roster (the shipped talent-build reference's heroes) and which are not; and the spread over roles, maps, leagues, regions and builds. Reads Data\\Focus\\ledger.json and Data\\youtube-library.jsonl. Read-only."
        )
    {
        var ledgerOption = new Option<string>("--ledger")
        {
            Description =
                "The focus ledger. Default Data\\Focus\\ledger.json under Location:DataDirectory.",
        };
        var libraryOption = new Option<string>("--library")
        {
            Description =
                "The YouTube library record, for which picks were uploaded. Default Data\\youtube-library.jsonl.",
        };
        Option<string> output = CliOutput.CreateOption(
            "JSON: schemaVersion, ok, code (focus.coverage), message, environment, details (picks, played, published, rosterSize, heroesCovered, uncovered[], and heroes[], roles[], maps[], leagues[], regions[], builds[] each with name, picks, published, share)."
        );
        Options.Add(ledgerOption);
        Options.Add(libraryOption);
        Options.Add(output);

        SetAction(parseResult =>
        {
            AppSettings settings = ServiceCollectionExtensions.LoadOfflineSettings();
            string data = settings.Location?.DataDirectory;
            return Run(
                parseResult.GetValue(ledgerOption) ?? FocusLedgerStore.PathFor(data),
                parseResult.GetValue(libraryOption) ?? YouTubeLibraryRecord.PathFor(data),
                Roster(),
                CliOutput.Format(parseResult, output),
                CliOutput.Out(parseResult)
            );
        });
    }

    /// <summary>Every hero name in the shipped talent-build reference.</summary>
    public static IReadOnlyList<string> Roster() =>
        GuideCatalog.Embedded().Reference.Heroes.Select(guide => guide.Hero).ToList();

    public static int Run(
        string ledgerPath,
        string libraryPath,
        IReadOnlyList<string> roster,
        CliOutputFormat format,
        TextWriter output
    )
    {
        FocusLedger ledger = File.Exists(ledgerPath)
            ? new FocusLedgerStore(ledgerPath).Read()
            : new FocusLedger();
        FocusCoverageReport report = FocusCoverage.Build(
            ledger,
            roster,
            FocusCoverage.PublishedReplayIds(libraryPath)
        );
        string message =
            $"{report.Picks} focus picks ({report.Played} played, {report.Published} uploaded); "
            + $"{report.HeroesCovered} of {report.RosterSize} heroes covered.";
        if (format == CliOutputFormat.Json)
        {
            return CliOutput.WriteJson(
                new CliResult<FocusCoverageReport>
                {
                    Ok = true,
                    Code = Read,
                    Message = message,
                    Environment = CliJson.CurrentEnvironment(),
                    Details = report,
                },
                output
            );
        }

        output.WriteLine(message);
        if (report.Uncovered.Count > 0)
        {
            output.WriteLine($"Not covered yet: {string.Join(", ", report.Uncovered)}.");
        }

        Write(output, "Roles", report.Roles);
        Write(output, "Maps", report.Maps);
        Write(output, "Leagues", report.Leagues);
        Write(output, "Regions", report.Regions);
        Write(output, "Heroes", report.Heroes);
        Write(output, "Builds", report.Builds);
        return 0;
    }

    private static void Write(
        TextWriter output,
        string title,
        IReadOnlyList<FocusCoverageCount> counts
    )
    {
        if (counts.Count == 0)
        {
            return;
        }

        output.WriteLine(
            $"{title}: "
                + string.Join(
                    ", ",
                    counts.Select(count =>
                        count.Published > 0
                            ? $"{count.Name} {count.Picks} ({count.Published} up)"
                            : $"{count.Name} {count.Picks}"
                    )
                )
        );
    }
}
