using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

public static class ObsCollectionPatcher
{
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false
    );

    public static ObsCollectionApplyResult ApplyForInstall(
        string startDirectory,
        string dataDirectory,
        bool obsIsRunning
    )
    {
        string template = ObsCollectionPaths.FindCollection(startDirectory);
        if (template == null)
        {
            return ObsCollectionApplyResult.Drifted(
                "OBS collection was not found next to the install."
            );
        }

        return Apply(template, LiveCollectionPath(null), dataDirectory, obsIsRunning);
    }

    public static string LiveCollectionPath(string appData)
    {
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        return Path.Combine(appData, "obs-studio", "basic", "scenes", "HeroesReplay.json");
    }

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

    public static ObsCollectionApplyResult Apply(
        string templatePath,
        string destinationPath,
        string dataDirectory,
        bool obsIsRunning
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
            File.WriteAllText(
                destinationFull,
                ObsCollectionPaths.Rewrite(template, assetRoot, dataDirectory),
                Utf8
            );
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

    private static bool IsCustom(string templateJson, string liveJson)
    {
        try
        {
            var packaged = new HashSet<string>(
                ObsCollectionPaths.SourceNames(templateJson),
                StringComparer.Ordinal
            );
            var live = new HashSet<string>(
                ObsCollectionPaths.SourceNames(liveJson),
                StringComparer.Ordinal
            );
            return !packaged.SetEquals(live);
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
            var packaged = new HashSet<string>(
                ObsCollectionPaths.SourceNames(templateJson),
                StringComparer.Ordinal
            );
            var live = new HashSet<string>(
                ObsCollectionPaths.SourceNames(liveJson),
                StringComparer.Ordinal
            );
            string extra = string.Join(", ", live.Where(name => !packaged.Contains(name)).Take(8));
            string missing = string.Join(
                ", ",
                packaged.Where(name => !live.Contains(name)).Take(8)
            );
            return "The live OBS collection does not match the packaged scenes (extra: "
                + extra
                + "; missing: "
                + missing
                + "). It was not overwritten.";
        }
        catch (JsonException)
        {
            return "The live OBS collection could not be compared. It was not overwritten.";
        }
    }
}

public sealed record ObsCollectionApplyResult(bool Wrote, bool Drift, string Message)
{
    public static ObsCollectionApplyResult Installed(string message) => new(true, false, message);

    public static ObsCollectionApplyResult Updated(string message) => new(true, false, message);

    public static ObsCollectionApplyResult Unchanged(string message) => new(false, false, message);

    public static ObsCollectionApplyResult Drifted(string message) => new(false, true, message);
}
