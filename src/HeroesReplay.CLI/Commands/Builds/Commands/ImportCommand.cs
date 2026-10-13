using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Builds.Import;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.CLI.Commands.Builds.Commands;

/// <summary>
/// Turns the <c>icy-veins-builds</c> skill's extract into the talent-build reference (#412),
/// after checking every tier against the heroes-data catalog's talents.
/// </summary>
public class ImportCommand : Command
{
    public ImportCommand()
        : base(
            "import",
            "Check the icy-veins-builds skill's extract (build names, tags, per-tier marks) against the heroes-data2 catalog's talents under Location:DataDirectory and write the talent-build reference JSON. Prints every warning (a stale tier, a renamed talent, a hero with no guide). Refuses to write when a guide names no catalog hero, a build lacks seven tiers, or a mark is not y, s, or n. Reads nothing from the web."
        )
    {
        var inputOption = new Option<string>("--input")
        {
            Description = "The skill's extract: a JSON array of guides.",
            Required = true,
        };
        inputOption.Aliases.Add("-i");

        var referenceOption = new Option<string>("--reference")
        {
            Description =
                "The reference file to write, normally src/HeroesReplay.Core/Builds/Reference/icy-veins-builds.json.",
            Required = true,
        };
        referenceOption.Aliases.Add("-r");

        var checkedOption = new Option<string>("--checked-at")
        {
            Description = "The day the guides were read (yyyy-MM-dd). Defaults to today (UTC).",
        };

        Options.Add(inputOption);
        Options.Add(referenceOption);
        Options.Add(checkedOption);

        SetAction(
            (parseResult, cancellationToken) =>
                RunAsync(
                    parseResult.GetValue(inputOption),
                    parseResult.GetValue(referenceOption),
                    parseResult.GetValue(checkedOption),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error,
                    cancellationToken
                )
        );
    }

    internal static async Task<int> RunAsync(
        string input,
        string reference,
        string checkedAt,
        TextWriter console,
        TextWriter error,
        CancellationToken cancellationToken
    )
    {
        console ??= Console.Out;
        error ??= Console.Error;
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            error.WriteLine($"Extract was not found: {input}");
            return 1;
        }

        DateOnly day = DateOnly.FromDateTime(DateTime.UtcNow);
        if (
            !string.IsNullOrWhiteSpace(checkedAt)
            && !DateOnly.TryParseExact(
                checkedAt,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out day
            )
        )
        {
            error.WriteLine($"--checked-at must be yyyy-MM-dd: {checkedAt}");
            return 1;
        }

        IReadOnlyList<GuideExtractHero> extract;
        try
        {
            extract = GuideExtractJson.Parse(
                await File.ReadAllTextAsync(input, cancellationToken).ConfigureAwait(false)
            );
        }
        catch (JsonException e)
        {
            error.WriteLine($"Extract is not valid JSON: {e.Message}");
            return 1;
        }

        AppSettings settings = ServiceCollectionExtensions.LoadOfflineSettings();
        var data = new GameData(NullLogger<GameData>.Instance, settings);
        await data.LoadDataAsync().ConfigureAwait(false);
        int code = Run(
            extract,
            data.Heroes,
            day,
            GameVersion(settings.HeroesDataPath),
            reference,
            console,
            error
        );
        return code;
    }

    /// <summary>Imports <paramref name="extract"/> against <paramref name="catalog"/> and writes <paramref name="reference"/>. Exit 0 written, 2 refused.</summary>
    public static int Run(
        IReadOnlyList<GuideExtractHero> extract,
        IReadOnlyList<Hero> catalog,
        DateOnly checkedAt,
        string gameVersion,
        string reference,
        TextWriter console,
        TextWriter error
    )
    {
        GuideImportResult result = GuideImporter.Import(extract, catalog, checkedAt, gameVersion);
        foreach (string warning in result.Warnings)
        {
            console.WriteLine("warning: " + warning);
        }

        if (result.Reference == null)
        {
            foreach (string problem in result.Errors)
            {
                error.WriteLine("error: " + problem);
            }

            error.WriteLine($"Nothing written: {result.Errors.Count} error(s).");
            return 2;
        }

        string folder = Path.GetDirectoryName(Path.GetFullPath(reference));
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(reference, GuideReferenceJson.Write(result.Reference));
        IReadOnlyList<GuideBuild> builds = result
            .Reference.Heroes.SelectMany(guide => guide.Builds)
            .ToList();
        console.WriteLine(
            $"Wrote {result.Reference.Heroes.Count} heroes and {builds.Count} builds to {reference} "
                + $"({result.Warnings.Count} warning(s)). Tags: "
                + string.Join(
                    ", ",
                    builds
                        .GroupBy(build => build.Tag ?? "none")
                        .OrderByDescending(group => group.Count())
                        .Select(group => $"{group.Key} {group.Count()}")
                )
                + "."
        );
        return 0;
    }

    /// <summary>The newest heroes-data file's game version (<c>meta.heroesVersion</c>), or null.</summary>
    private static string GameVersion(string heroesDataPath)
    {
        if (string.IsNullOrWhiteSpace(heroesDataPath) || !Directory.Exists(heroesDataPath))
        {
            return null;
        }

        string file = Directory
            .EnumerateFiles(heroesDataPath, "herodata_*.json", SearchOption.AllDirectories)
            .OrderByDescending(path => BuildNumber(Path.GetFileNameWithoutExtension(path)))
            .FirstOrDefault();
        if (file == null)
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
        return
            document.RootElement.TryGetProperty("meta", out JsonElement meta)
            && meta.TryGetProperty("heroesVersion", out JsonElement version)
            ? version.GetString()
            : null;
    }

    /// <summary>The build in <c>herodata_98304</c> or <c>herodata_98304_localized</c>, or 0.</summary>
    private static int BuildNumber(string name)
    {
        string[] parts = name.Split('_');
        return
            parts.Length > 1
            && int.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int build
            )
            ? build
            : 0;
    }
}
