using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Obs.Inspection;
using Microsoft.Extensions.Configuration;

namespace HeroesReplay.CLI.Commands.Obs;

/// <summary>The JSON of <c>obs bundle --output json</c>.</summary>
public sealed record ObsBundleReport(
    int SchemaVersion,
    bool Ok,
    string Code,
    string Format,
    string Manifest,
    int Files,
    IReadOnlyList<ObsBundleProblem> Problems,
    string Message
);

/// <summary>
/// <c>obs bundle</c>: the install's OBS files against <c>obs\bundle.manifest</c>, without OBS.
/// <c>--write</c> is the packaging step: <c>tools/package-release.ps1</c> runs the published
/// build on its publish folder to turn the plain asset list into the schema 2 manifest.
/// </summary>
public static class ObsBundleCommand
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Command Create()
    {
        var command = new Command(
            "bundle",
            "Check this install's OBS files against obs\\bundle.manifest without connecting to OBS: each file's size and SHA-256, and that obs\\Default.json has the scene and source contract. Exit 1 on obs.bundle_invalid. A source checkout's plain asset list is checked for presence only; a folder with no manifest is reported and exits 0. --write is the packaging step tools/package-release.ps1 runs on its publish folder."
        );
        Option<string> install = new("--install")
        {
            Description =
                "The install or publish folder that holds obs\\Default.json. Default: this exe's folder (a source build finds the checkout's obs folder).",
        };
        Option<bool> write = new("--write")
        {
            Description =
                "Write obs\\bundle.manifest (schema 2) in --install from the paths its current manifest lists: each file's size and SHA-256, Default.json's hash, and the scene and source contract from that folder's appsettings.json and appsettings.prod.json. Refuses a source checkout. Exit 1 when a listed file is missing or obs\\Default.json lacks a contract name.",
        };
        var format = new Option<string>("--output")
        {
            Description = "text (default) or json: schemaVersion, ok, code, format, and problems.",
            DefaultValueFactory = _ => "text",
        };
        format.AcceptOnlyFromAmong("text", "json");
        format.Aliases.Add("-o");
        command.Options.Add(install);
        command.Options.Add(write);
        command.Options.Add(format);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string folder = parseResult.GetValue(install);
                bool json = string.Equals(
                    parseResult.GetValue(format),
                    "json",
                    StringComparison.OrdinalIgnoreCase
                );
                return Task.FromResult(
                    parseResult.GetValue(write)
                        ? Write(folder ?? AppContext.BaseDirectory, Console.Out, Console.Error)
                        : Check(folder ?? AppContext.BaseDirectory, json, Console.Out)
                );
            }
        );
        return command;
    }

    /// <summary>Checks the <c>obs</c> folder in or above <paramref name="install"/>.</summary>
    public static int Check(string install, bool json, TextWriter output)
    {
        string collection = ObsCollectionPaths.FindCollection(install);
        ObsBundleReport report;
        if (collection == null)
        {
            report = new ObsBundleReport(
                1,
                false,
                ObsValidator.BundleMissing,
                null,
                null,
                0,
                [],
                "obs\\Default.json was not found in or above " + install + "."
            );
        }
        else
        {
            ObsBundleCheck check = ObsCollectionBundle.Verify(Path.GetDirectoryName(collection));
            report = new ObsBundleReport(
                1,
                check.Ok,
                check.Ok ? null : ObsCollectionBundle.InvalidCode,
                JsonNamingPolicy.CamelCase.ConvertName(check.Format.ToString()),
                check.ManifestPath,
                check.Files,
                check.Problems,
                check.Describe()
            );
        }

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(report, Json));
        }
        else
        {
            output.WriteLine(report.Manifest == null ? report.Message : report.Manifest + ":");
            if (report.Manifest != null)
            {
                output.WriteLine(report.Message);
            }

            foreach (ObsBundleProblem problem in report.Problems)
            {
                output.WriteLine($"error {report.Code} {problem.Path}: {problem.Reason}");
            }
        }

        return report.Ok ? 0 : 1;
    }

    /// <summary>
    /// Writes the schema 2 manifest in <paramref name="install"/><c>\obs</c> from the paths its
    /// current manifest lists. Only for a publish folder: the checkout's file is the plain list
    /// the build reads.
    /// </summary>
    public static int Write(string install, TextWriter output, TextWriter error)
    {
        string root = Path.GetFullPath(install);
        string obs = Path.Combine(root, "obs");
        string manifestPath = Path.Combine(obs, ObsCollectionBundle.FileName);
        if (File.Exists(Path.Combine(root, "heroes-replay.slnx")))
        {
            error.WriteLine(
                root
                    + " is a source checkout. Its obs\\bundle.manifest is the plain asset list the build publishes; --write is for a publish folder."
            );
            return 1;
        }

        if (!File.Exists(Path.Combine(obs, ObsCollectionBundle.CollectionFileName)))
        {
            error.WriteLine($"No obs\\Default.json in {root}.");
            return 1;
        }

        if (!File.Exists(manifestPath))
        {
            error.WriteLine(
                $"No {manifestPath}. Copy the checkout's obs\\bundle.manifest (the asset list) there first."
            );
            return 1;
        }

        try
        {
            ObsContract contract = ObsContract.From(PackagedObsSettings(root));
            ObsBundleManifest manifest = ObsCollectionBundle.Create(
                obs,
                ObsCollectionBundle.AssetPaths(obs),
                contract
            );
            ObsBundleCheck check = ObsCollectionBundle.Verify(obs, manifest, manifestPath);
            if (!check.Ok)
            {
                error.WriteLine(check.Describe() + " The manifest was not written.");
                return 1;
            }

            string temp = manifestPath + ".tmp";
            File.WriteAllText(temp, ObsCollectionBundle.Serialize(manifest));
            File.Move(temp, manifestPath, overwrite: true);
            output.WriteLine(
                $"Wrote {manifestPath} (schema {ObsCollectionBundle.SchemaVersion}): {manifest.Assets.Count} files, {contract.Scenes.Count} scenes, {contract.Sources.Count} sources, {contract.Items.Count} scene items."
            );
            return 0;
        }
        catch (Exception e)
            when (e
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or InvalidDataException
                        or FormatException
            )
        {
            error.WriteLine($"obs\\bundle.manifest was not written. {e.Message}");
            return 1;
        }
    }

    /// <summary>
    /// The release's OBS names: the folder's <c>appsettings.json</c> and the prod overlay it ships,
    /// without the packaging shell's <c>HEROES_REPLAY_</c> variables or any secret.
    /// </summary>
    private static OBSSettings PackagedObsSettings(string root) =>
        new ConfigurationBuilder()
            .SetBasePath(root)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.prod.json", optional: true)
            .Build()
            .GetSection("OBS")
            .Get<OBSSettings>()
        ?? new OBSSettings();
}
