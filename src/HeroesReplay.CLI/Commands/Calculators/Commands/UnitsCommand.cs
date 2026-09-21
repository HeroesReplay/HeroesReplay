using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Services.Analysis.Reports;

namespace HeroesReplay.CLI.Commands.Calculators.Commands;

public class UnitsCommand : Command
{
    public UnitsCommand()
        : base(
            "units",
            "Sample up to 5 replays per map and write unit CSV reports. Parses one file at a time."
        )
    {
        var directoryOption = new Option<string>("--directory")
        {
            Description =
                "Directory of .StormReplay files. Only the top level is scanned, one file at a time.",
            Required = true,
        };
        directoryOption.Aliases.Add("-d");

        var perMapOption = new Option<int>("--per-map")
        {
            Description = "Replays to parse fully for each map. Must be from 1 to 5.",
            DefaultValueFactory = _ => 5,
        };
        perMapOption.Aliases.Add("-n");

        var outputOption = new Option<string>("--output")
        {
            Description =
                "Directory for survey-maps.csv, samples.csv, units-by-map.csv, candidates.csv, and failures.csv. Defaults to a unit-reports folder inside --directory.",
        };
        outputOption.Aliases.Add("-o");

        Options.Add(directoryOption);
        Options.Add(perMapOption);
        Options.Add(outputOption);

        SetAction(
            (parseResult, cancellationToken) =>
                RunAsync(
                    parseResult.GetValue(directoryOption),
                    parseResult.GetValue(perMapOption),
                    parseResult.GetValue(outputOption),
                    cancellationToken
                )
        );
    }

    public static CalculatorUnitLists LoadCalculatorLists(string settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
        {
            return new CalculatorUnitLists();
        }

        using (
            JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(settingsPath),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                }
            )
        )
        {
            if (!document.RootElement.TryGetProperty("HeroesToolChest", out JsonElement chest))
            {
                return new CalculatorUnitLists();
            }

            return new CalculatorUnitLists
            {
                ObjectiveContains = ReadList(chest, "ObjectiveContains"),
                BossContains = ReadList(chest, "BossContains"),
                CampContains = ReadList(chest, "CampContains"),
                VehicleContains = ReadList(chest, "VehicleContains"),
                CaptureContains = ReadList(chest, "CaptureContains"),
                IgnoreContains = ReadList(chest, "IgnoreUnits"),
            };
        }
    }

    internal static async Task<int> RunAsync(
        string directory,
        int perMap,
        string output,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            Console.Error.WriteLine($"Replay directory was not found: {directory}");
            return 1;
        }

        if (perMap < 1 || perMap > 5)
        {
            Console.Error.WriteLine("--per-map must be from 1 to 5 so a map sample stays small.");
            return 1;
        }

        string outputDirectory = string.IsNullOrWhiteSpace(output)
            ? Path.Combine(directory, "unit-reports")
            : output;

        List<string> files = new DirectoryInfo(directory)
            .EnumerateFiles("*.StormReplay", SearchOption.TopDirectoryOnly)
            .Select(file => file.FullName)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            Console.Error.WriteLine($"No .StormReplay files were found in {directory}");
            return 1;
        }

        CalculatorUnitLists lists = LoadCalculatorLists(FindSettingsPath());
        var sampler = new MapReplaySampler(perMap);
        var inventory = new UnitInventory();
        var samples = new List<ReplaySampleRow>();
        var failures = new List<ReplayFailureRow>();
        var selected = new List<SurveyHit>();
        var watch = Stopwatch.StartNew();

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        IReadOnlyList<string> catalog = LoadCatalogNames(FindSettingsPath());
        Console.WriteLine(
            $"Surveying {files.Count} replays, one file at a time. Localized titles are merged onto the English map catalog, then up to {perMap} replays are parsed per map."
        );

        for (int index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = files[index];
            SurveyHit hit = ReadSurvey(path);
            if (!hit.Ok)
            {
                failures.Add(
                    new ReplayFailureRow
                    {
                        Stage = "survey",
                        Path = path,
                        Message = hit.Error,
                    }
                );
            }
            else if (sampler.TryTake(hit.Map, path))
            {
                hit.Path = path;
                selected.Add(hit);
                Console.WriteLine(
                    $"[survey {index + 1}/{files.Count}] selected {hit.Map} ({Path.GetFileName(path)})"
                );
            }

            if ((index + 1) % 25 == 0 || index + 1 == files.Count)
            {
                Console.WriteLine(
                    $"[survey {index + 1}/{files.Count}] localized={selected.Select(item => item.Map).Distinct(StringComparer.OrdinalIgnoreCase).Count()} kept={selected.Count} failures={failures.Count}"
                );
                Console.Out.Flush();
            }

            if ((index + 1) % 100 == 0)
            {
                WriteReport(
                    outputDirectory,
                    sampler.Selected(),
                    samples,
                    inventory,
                    failures,
                    lists
                );
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            }
        }

        Console.WriteLine(
            $"Survey kept {selected.Count} replays across {sampler.Selected().Count} localized titles in {watch.Elapsed:hh\\:mm\\:ss}."
        );
        WriteReport(outputDirectory, sampler.Selected(), samples, inventory, failures, lists);

        if (selected.Count == 0)
        {
            Console.Error.WriteLine("No replay advertised a map. See failures.csv.");
            return 2;
        }

        List<LocaleSample> locales = FingerprintLocales(selected, failures, cancellationToken);
        AssignCanonicalMaps(locales, catalog);
        List<PlannedReplay> planned = PlanParses(locales, perMap);
        var plannedPaths = new HashSet<string>(
            planned.Select(plan => plan.Path),
            StringComparer.OrdinalIgnoreCase
        );
        foreach (LocaleSample locale in locales)
        {
            if (
                locale.Detail != null
                && locale.Parsed != null
                && !plannedPaths.Contains(locale.Parsed.Path)
            )
            {
                locale.Detail.Units = null;
            }
        }

        IReadOnlyList<MapReplaySample> canonicalMaps = CanonicalSamples(locales, planned, sampler);
        Console.WriteLine(
            $"Parsing units for {planned.Count} replays across {canonicalMaps.Count} maps, one file at a time."
        );

        for (int index = 0; index < planned.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlannedReplay plan = planned[index];
            Console.WriteLine(
                $"[detail {index + 1}/{planned.Count}] {plan.Canonical} ({plan.Localized}) {Path.GetFileName(plan.Path)}"
            );
            Console.Out.Flush();

            DetailRead detail = plan.Detail;
            if (detail == null)
            {
                detail = ReadDetail(plan.Path);
            }

            var sample = new ReplaySampleRow
            {
                Map = plan.Canonical,
                LocalizedMap = plan.Localized,
                Path = plan.Path,
                SurveyResult = plan.SurveyResult,
                DetailResult = detail.Result,
                ReplayVersion = string.IsNullOrWhiteSpace(detail.Version)
                    ? plan.Version
                    : detail.Version,
                ReplayLengthSeconds = detail.LengthSeconds,
                UnitInstances = detail.UnitInstances,
                DistinctUnits = detail.DistinctUnits,
            };
            samples.Add(sample);

            if (!string.IsNullOrEmpty(detail.Error))
            {
                failures.Add(
                    new ReplayFailureRow
                    {
                        Stage = "detail",
                        Path = plan.Path,
                        Message = detail.Error,
                    }
                );
            }
            else
            {
                inventory.AddReplay(plan.Canonical, sample.ReplayVersion, detail.Units);
            }

            detail.Units = null;
            plan.Detail = null;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            WriteReport(outputDirectory, canonicalMaps, samples, inventory, failures, lists);
        }

        watch.Stop();
        Console.WriteLine(
            $"Wrote unit reports to {outputDirectory} in {watch.Elapsed:hh\\:mm\\:ss}."
        );
        Console.WriteLine($"Failures: {failures.Count}. Candidates are the units worth reviewing.");
        return failures.Count > 0 && inventory.Rows().Count == 0 ? 2 : 0;
    }

    private static void WriteReport(
        string outputDirectory,
        IReadOnlyList<MapReplaySample> survey,
        List<ReplaySampleRow> samples,
        UnitInventory inventory,
        List<ReplayFailureRow> failures,
        CalculatorUnitLists lists
    )
    {
        UnitReportWriter.Write(outputDirectory, survey, samples, inventory.Rows(), failures, lists);
    }

    private static List<LocaleSample> FingerprintLocales(
        List<SurveyHit> selected,
        List<ReplayFailureRow> failures,
        CancellationToken cancellationToken
    )
    {
        List<IGrouping<string, SurveyHit>> groups = selected
            .GroupBy(hit => hit.Map, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var locales = new List<LocaleSample>();
        Console.WriteLine($"Fingerprinting {groups.Count} localized titles, one file at a time.");
        for (int index = 0; index < groups.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<SurveyHit> hits = groups[index]
                .OrderBy(hit => hit.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            SurveyHit first = hits[0];
            Console.WriteLine(
                $"[fingerprint {index + 1}/{groups.Count}] {first.Map} {Path.GetFileName(first.Path)}"
            );
            Console.Out.Flush();
            DetailRead detail = ReadDetail(first.Path);
            if (!string.IsNullOrEmpty(detail.Error))
            {
                failures.Add(
                    new ReplayFailureRow
                    {
                        Stage = "fingerprint",
                        Path = first.Path,
                        Message = detail.Error,
                    }
                );
            }

            locales.Add(
                new LocaleSample
                {
                    Localized = first.Map,
                    Hits = hits,
                    Parsed = first,
                    Detail = detail,
                }
            );
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        }

        return locales;
    }

    private static void AssignCanonicalMaps(
        List<LocaleSample> locales,
        IReadOnlyList<string> catalog
    )
    {
        var knownUnits = new Dictionary<string, IReadOnlyCollection<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (LocaleSample locale in locales)
        {
            string catalogName = CatalogName(catalog, locale.Localized);
            if (catalogName == null)
            {
                continue;
            }

            locale.Canonical = catalogName;
            knownUnits[catalogName] = DistinctiveUnits(locale.Detail);
        }

        IReadOnlyDictionary<string, string> markers = MapIdentity.UniqueMarkers(knownUnits);
        foreach (LocaleSample locale in locales)
        {
            if (locale.Canonical == null)
            {
                locale.Canonical = MapIdentity.Match(
                    DistinctiveUnits(locale.Detail),
                    markers,
                    locale.Localized
                );
            }

            Console.WriteLine($"[map] {locale.Localized} -> {locale.Canonical}");
        }
    }

    private static List<PlannedReplay> PlanParses(List<LocaleSample> locales, int perMap)
    {
        var planned = new List<PlannedReplay>();
        foreach (
            IGrouping<string, LocaleSample> group in locales
                .GroupBy(
                    locale => locale.Canonical ?? locale.Localized,
                    StringComparer.OrdinalIgnoreCase
                )
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        )
        {
            var files = new List<PlannedReplay>();
            IEnumerable<LocaleSample> ordered = group
                .OrderBy(locale =>
                    string.Equals(
                        locale.Localized,
                        locale.Canonical,
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? 0
                        : 1
                )
                .ThenBy(locale => locale.Localized, StringComparer.OrdinalIgnoreCase);
            foreach (LocaleSample locale in ordered)
            {
                foreach (SurveyHit hit in locale.Hits)
                {
                    bool parsedThisFile =
                        locale.Detail != null
                        && locale.Parsed != null
                        && string.Equals(
                            hit.Path,
                            locale.Parsed.Path,
                            StringComparison.OrdinalIgnoreCase
                        );
                    if (parsedThisFile && !string.IsNullOrEmpty(locale.Detail.Error))
                    {
                        continue;
                    }

                    files.Add(
                        new PlannedReplay
                        {
                            Canonical = group.Key,
                            Localized = locale.Localized,
                            Path = hit.Path,
                            SurveyResult = hit.Result,
                            Version = hit.Version,
                            Detail = parsedThisFile ? locale.Detail : null,
                        }
                    );
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int taken = 0;
            foreach (PlannedReplay file in files)
            {
                if (!seen.Add(file.Path))
                {
                    continue;
                }

                planned.Add(file);
                taken++;
                if (taken == perMap)
                {
                    break;
                }
            }
        }

        return planned;
    }

    private static IReadOnlyList<MapReplaySample> CanonicalSamples(
        List<LocaleSample> locales,
        List<PlannedReplay> planned,
        MapReplaySampler sampler
    )
    {
        var identified = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (MapReplaySample sample in sampler.Selected())
        {
            identified[sample.Map] = sample.FilesIdentified;
        }

        var rows = new List<MapReplaySample>();
        foreach (
            IGrouping<string, PlannedReplay> group in planned
                .GroupBy(plan => plan.Canonical, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        )
        {
            int filesIdentified = 0;
            foreach (LocaleSample locale in locales)
            {
                if (!string.Equals(locale.Canonical, group.Key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int count;
                if (identified.TryGetValue(locale.Localized, out count))
                {
                    filesIdentified += count;
                }
            }

            rows.Add(
                new MapReplaySample(
                    group.Key,
                    group.Select(plan => plan.Path).ToList(),
                    filesIdentified
                )
            );
        }

        return rows;
    }

    private static IReadOnlyCollection<string> DistinctiveUnits(DetailRead detail)
    {
        if (detail == null || detail.Units == null || !string.IsNullOrEmpty(detail.Error))
        {
            return new string[0];
        }

        return detail
            .Units.Where(unit => MapIdentity.CanIdentifyMap(unit.Name, unit.Group))
            .Select(unit => unit.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string CatalogName(IReadOnlyList<string> catalog, string localized)
    {
        if (catalog == null || string.IsNullOrWhiteSpace(localized))
        {
            return null;
        }

        foreach (string name in catalog)
        {
            if (string.Equals(name, localized, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> LoadCatalogNames(string settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
        {
            return new string[0];
        }

        using (
            JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(settingsPath),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                }
            )
        )
        {
            JsonElement maps;
            JsonElement catalog;
            if (
                !document.RootElement.TryGetProperty("Maps", out maps)
                || !maps.TryGetProperty("Catalog", out catalog)
                || catalog.ValueKind != JsonValueKind.Array
            )
            {
                return new string[0];
            }

            var names = new List<string>();
            foreach (JsonElement map in catalog.EnumerateArray())
            {
                JsonElement name;
                if (!map.TryGetProperty("Name", out name) || name.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string text = name.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    names.Add(text);
                }
            }

            return names;
        }
    }

    private static SurveyHit ReadSurvey(string path)
    {
        var hit = new SurveyHit();
        try
        {
            (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(
                path,
                false,
                SurveyOptions()
            );
            hit.Result =
                result == DataParser.ReplayParseResult.UnexpectedResult
                    ? "Parsed"
                    : result.ToString();
            if (replay == null || string.IsNullOrWhiteSpace(replay.Map))
            {
                hit.Error = hit.Result;
                return hit;
            }

            hit.Ok = true;
            hit.Map = replay.Map.Trim();
            hit.Version = replay.ReplayVersion;
            return hit;
        }
        catch (Exception exception)
        {
            hit.Result = "Exception";
            hit.Error = exception.GetType().Name + ": " + exception.Message;
            return hit;
        }
    }

    private static DetailRead ReadDetail(string path)
    {
        var detail = new DetailRead();
        try
        {
            (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(
                path,
                false,
                DetailOptions()
            );
            detail.Result =
                result == DataParser.ReplayParseResult.UnexpectedResult
                    ? "Parsed"
                    : result.ToString();
            if (replay == null)
            {
                detail.Error = detail.Result;
                return detail;
            }

            detail.Version = replay.ReplayVersion;
            detail.LengthSeconds = replay.ReplayLength.TotalSeconds.ToString("0.###");
            var units = new List<(string Name, string Group)>();
            var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Unit unit in replay.Units ?? new List<Unit>())
            {
                if (unit == null || string.IsNullOrWhiteSpace(unit.Name))
                {
                    continue;
                }

                units.Add((unit.Name, unit.Group.ToString()));
                distinct.Add(unit.Name);
            }

            detail.Units = units;
            detail.UnitInstances = units.Count;
            detail.DistinctUnits = distinct.Count;
            if (units.Count == 0)
            {
                detail.Error = "Parsed but no units were found.";
            }

            return detail;
        }
        catch (Exception exception)
        {
            detail.Result = "Exception";
            detail.Error = exception.GetType().Name + ": " + exception.Message;
            return detail;
        }
    }

    private static ParseOptions SurveyOptions()
    {
        return new ParseOptions
        {
            IgnoreErrors = true,
            AllowPTR = true,
            ShouldParseEvents = false,
            ShouldParseUnits = false,
            ShouldParseMouseEvents = false,
            ShouldParseDetailedBattleLobby = false,
            ShouldParseMessageEvents = false,
            ShouldParseStatistics = false,
        };
    }

    private static ParseOptions DetailOptions()
    {
        return new ParseOptions
        {
            IgnoreErrors = true,
            AllowPTR = true,
            ShouldParseEvents = true,
            ShouldParseUnits = true,
            ShouldParseMouseEvents = false,
            ShouldParseDetailedBattleLobby = false,
            ShouldParseMessageEvents = false,
            ShouldParseStatistics = false,
        };
    }

    private static string FindSettingsPath()
    {
        string current = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        if (File.Exists(current))
        {
            return current;
        }

        string deployed = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(deployed))
        {
            return deployed;
        }

        return null;
    }

    private static IReadOnlyList<string> ReadList(JsonElement chest, string name)
    {
        JsonElement array;
        if (!chest.TryGetProperty(name, out array) || array.ValueKind != JsonValueKind.Array)
        {
            return new string[0];
        }

        var values = new List<string>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string text = item.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                values.Add(text);
            }
        }

        return values;
    }

    private sealed class LocaleSample
    {
        public string Localized { get; set; }

        public string Canonical { get; set; }

        public List<SurveyHit> Hits { get; set; }

        public SurveyHit Parsed { get; set; }

        public DetailRead Detail { get; set; }
    }

    private sealed class PlannedReplay
    {
        public string Canonical { get; set; }

        public string Localized { get; set; }

        public string Path { get; set; }

        public string SurveyResult { get; set; }

        public string Version { get; set; }

        public DetailRead Detail { get; set; }
    }

    private sealed class SurveyHit
    {
        public bool Ok { get; set; }

        public string Map { get; set; }

        public string Path { get; set; }

        public string Result { get; set; }

        public string Version { get; set; }

        public string Error { get; set; }
    }

    private sealed class DetailRead
    {
        public string Result { get; set; }

        public string Version { get; set; }

        public string LengthSeconds { get; set; }

        public string Error { get; set; }

        public int UnitInstances { get; set; }

        public int DistinctUnits { get; set; }

        public List<(string Name, string Group)> Units { get; set; }
    }
}
