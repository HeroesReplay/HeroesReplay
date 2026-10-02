using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HeroesReplay.Core.Obs;

public static class ObsCollectionPatcher
{
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false
    );

    public static ObsCollectionApplyResult ApplyForInstall(
        string startDirectory,
        string dataDirectory,
        bool obsIsRunning,
        string collectionName = null
    )
    {
        string template = ObsCollectionPaths.FindCollection(startDirectory);
        if (template == null)
        {
            return ObsCollectionApplyResult.Drifted(
                "OBS collection was not found next to the install."
            );
        }

        return Apply(
            template,
            LiveCollectionPath(null, collectionName),
            dataDirectory,
            obsIsRunning,
            collectionName
        );
    }

    public static string LiveCollectionPath(string appData, string collectionName = null) =>
        ObsNames.CollectionFile(appData, collectionName);

    public static string ReadDataDirectory(string installDirectory)
    {
        const string fallback = @"C:\heroesreplay\Data";
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return fallback;
        }

        string path = Path.Combine(installDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            return fallback;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (
                document.RootElement.TryGetProperty("Location", out JsonElement location)
                && location.TryGetProperty("DataDirectory", out JsonElement data)
                && data.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(data.GetString())
            )
            {
                return data.GetString();
            }
        }
        catch (JsonException) { }
        catch (IOException) { }

        return fallback;
    }

    /// <param name="collectionName">
    /// Name written into a collection installed from the template. OBS lists a collection
    /// by that name. An existing live collection keeps its name.
    /// </param>
    public static ObsCollectionApplyResult Apply(
        string templatePath,
        string destinationPath,
        string dataDirectory,
        bool obsIsRunning,
        string collectionName = null
    )
    {
        if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
        {
            return ObsCollectionApplyResult.Drifted("OBS collection template was not found.");
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return ObsCollectionApplyResult.Drifted("OBS collection destination was not set.");
        }

        string templateFull = Path.GetFullPath(templatePath);
        string destinationFull = Path.GetFullPath(destinationPath);
        if (string.Equals(templateFull, destinationFull, StringComparison.OrdinalIgnoreCase))
        {
            return ObsCollectionApplyResult.Drifted(
                "Refusing to rewrite the packaged collection in place."
            );
        }

        string template = File.ReadAllText(templateFull);
        string assetRoot = Path.GetDirectoryName(templateFull);
        if (!File.Exists(destinationFull))
        {
            if (obsIsRunning)
            {
                return ObsCollectionApplyResult.Drifted(
                    "OBS is running, so the live collection was not created."
                );
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFull));
            string installed = ObsCollectionPaths.Rewrite(template, assetRoot, dataDirectory);
            if (!string.IsNullOrWhiteSpace(collectionName))
            {
                installed = ObsNames.WithCollectionName(installed, collectionName);
            }

            File.WriteAllText(destinationFull, installed, Utf8);
            return ObsCollectionApplyResult.Installed(
                "Installed the OBS collection for this install."
            );
        }

        string live;
        try
        {
            live = File.ReadAllText(destinationFull);
        }
        catch (IOException e)
        {
            return ObsCollectionApplyResult.Drifted(
                "The live OBS collection could not be read. It was not overwritten. " + e.Message
            );
        }

        if (obsIsRunning)
        {
            if (NeedsPathUpdate(live, assetRoot, dataDirectory) || IsCustom(template, live))
            {
                return ObsCollectionApplyResult.Drifted(
                    "OBS is running. The live collection was left unchanged."
                );
            }

            return ObsCollectionApplyResult.Unchanged(
                "OBS is running. Collection paths already match this install."
            );
        }

        if (IsCustom(template, live))
        {
            return ObsCollectionApplyResult.Drifted(DescribeDrift(template, live));
        }

        string updated;
        try
        {
            updated = ObsCollectionPaths.Rewrite(live, assetRoot, dataDirectory);
        }
        catch (JsonException)
        {
            return ObsCollectionApplyResult.Drifted(
                "The live OBS collection is not valid JSON. It was not overwritten."
            );
        }

        if (string.Equals(updated, live, StringComparison.Ordinal))
        {
            return ObsCollectionApplyResult.Unchanged(
                "OBS collection paths already match this install."
            );
        }

        File.WriteAllText(destinationFull, updated, Utf8);
        return ObsCollectionApplyResult.Updated("Updated OBS collection paths for this install.");
    }

    private static bool NeedsPathUpdate(string json, string assetRoot, string dataDirectory)
    {
        try
        {
            return !string.Equals(
                ObsCollectionPaths.Rewrite(json, assetRoot, dataDirectory),
                json,
                StringComparison.Ordinal
            );
        }
        catch (JsonException)
        {
            return true;
        }
    }

    /// <summary>
    /// Source names (scenes included) in the live collection and not in the package, and the
    /// reverse. Any difference makes the live collection custom: it is not rewritten.
    /// </summary>
    public static ObsNameDrift Drift(IEnumerable<string> packaged, IEnumerable<string> live)
    {
        var packagedSet = new HashSet<string>(packaged ?? [], StringComparer.Ordinal);
        var liveSet = new HashSet<string>(live ?? [], StringComparer.Ordinal);
        return new ObsNameDrift(
            liveSet.Where(name => !packagedSet.Contains(name)).ToList(),
            packagedSet.Where(name => !liveSet.Contains(name)).ToList()
        );
    }

    private static bool IsCustom(string templateJson, string liveJson)
    {
        try
        {
            return Drift(
                ObsCollectionPaths.SourceNames(templateJson),
                ObsCollectionPaths.SourceNames(liveJson)
            ).Custom;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static string DescribeDrift(string templateJson, string liveJson)
    {
        try
        {
            ObsNameDrift drift = Drift(
                ObsCollectionPaths.SourceNames(templateJson),
                ObsCollectionPaths.SourceNames(liveJson)
            );
            return "The live OBS collection does not match the packaged scenes (extra: "
                + string.Join(", ", drift.Extra.Take(8))
                + "; missing: "
                + string.Join(", ", drift.Missing.Take(8))
                + "). It was not overwritten.";
        }
        catch (JsonException)
        {
            return "The live OBS collection could not be compared. It was not overwritten.";
        }
    }
}

public sealed record ObsNameDrift(IReadOnlyList<string> Extra, IReadOnlyList<string> Missing)
{
    public bool Custom => Extra.Count > 0 || Missing.Count > 0;
}

public sealed record ObsCollectionApplyResult(bool Wrote, bool Drift, string Message)
{
    public static ObsCollectionApplyResult Installed(string message) => new(true, false, message);

    public static ObsCollectionApplyResult Updated(string message) => new(true, false, message);

    public static ObsCollectionApplyResult Unchanged(string message) => new(false, false, message);

    public static ObsCollectionApplyResult Drifted(string message) => new(false, true, message);
}
