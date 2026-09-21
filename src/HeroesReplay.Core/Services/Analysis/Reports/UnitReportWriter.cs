using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HeroesReplay.Core.Services.Analysis.Reports;

public sealed class ReplaySampleRow
{
    public string Map { get; set; }

    public string LocalizedMap { get; set; }

    public string Path { get; set; }

    public string SurveyResult { get; set; }

    public string DetailResult { get; set; }

    public string ReplayVersion { get; set; }

    public string ReplayLengthSeconds { get; set; }

    public int UnitInstances { get; set; }

    public int DistinctUnits { get; set; }
}

public sealed class ReplayFailureRow
{
    public string Stage { get; set; }

    public string Path { get; set; }

    public string Message { get; set; }
}

public static class UnitReportWriter
{
    private static readonly UTF8Encoding Encoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false
    );

    public static void Write(
        string directory,
        IReadOnlyList<MapReplaySample> survey,
        IReadOnlyList<ReplaySampleRow> samples,
        IReadOnlyList<UnitInventoryRow> units,
        IReadOnlyList<ReplayFailureRow> failures,
        CalculatorUnitLists lists
    )
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("An output directory is required.", nameof(directory));
        }

        Directory.CreateDirectory(directory);
        lists = lists ?? new CalculatorUnitLists();

        WriteLines(
            Path.Combine(directory, "survey-maps.csv"),
            "map,files_identified,files_selected",
            survey,
            sample =>
                string.Join(
                    ",",
                    Cell(sample.Map),
                    sample.FilesIdentified.ToString(),
                    sample.Paths.Count.ToString()
                )
        );

        WriteLines(
            Path.Combine(directory, "samples.csv"),
            "map,localized_map,path,survey_result,detail_result,replay_version,replay_length_seconds,unit_instances,distinct_units",
            samples,
            sample =>
                string.Join(
                    ",",
                    Cell(sample.Map),
                    Cell(sample.LocalizedMap),
                    Cell(sample.Path),
                    Cell(sample.SurveyResult),
                    Cell(sample.DetailResult),
                    Cell(sample.ReplayVersion),
                    Cell(sample.ReplayLengthSeconds),
                    sample.UnitInstances.ToString(),
                    sample.DistinctUnits.ToString()
                )
        );

        WriteLines(
            Path.Combine(directory, "units-by-map.csv"),
            "map,unit_name,parser_group,occurrences,replays_with_unit,replays_sampled,replay_versions,interest,calculator_lists",
            units,
            unit => FormatUnit(unit, lists)
        );

        var candidates = new List<UnitInventoryRow>();
        foreach (UnitInventoryRow unit in units)
        {
            if (UnitInterest.IsCandidate(unit.Name, unit.ParserGroup, lists))
            {
                candidates.Add(unit);
            }
        }

        WriteLines(
            Path.Combine(directory, "candidates.csv"),
            "map,unit_name,parser_group,occurrences,replays_with_unit,replays_sampled,replay_versions,interest,calculator_lists",
            candidates,
            unit => FormatUnit(unit, lists)
        );

        WriteLines(
            Path.Combine(directory, "failures.csv"),
            "stage,path,message",
            failures,
            failure =>
                string.Join(",", Cell(failure.Stage), Cell(failure.Path), Cell(failure.Message))
        );
    }

    private static string FormatUnit(UnitInventoryRow unit, CalculatorUnitLists lists)
    {
        return string.Join(
            ",",
            Cell(unit.Map),
            Cell(unit.Name),
            Cell(unit.ParserGroup),
            unit.Occurrences.ToString(),
            unit.ReplaysWithUnit.ToString(),
            unit.ReplaysSampled.ToString(),
            Cell(unit.ReplayVersions),
            Cell(string.Join(";", UnitInterest.Tags(unit.Name))),
            Cell(UnitInterest.CalculatorLists(unit.Name, lists))
        );
    }

    private static void WriteLines<T>(
        string path,
        string header,
        IReadOnlyList<T> rows,
        Func<T, string> format
    )
    {
        using (var writer = new StreamWriter(path, append: false, Encoding))
        {
            writer.WriteLine(header);
            if (rows == null)
            {
                return;
            }

            foreach (T row in rows)
            {
                writer.WriteLine(format(row));
            }
        }
    }

    internal static string Cell(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool mustQuote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        if (!mustQuote)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
