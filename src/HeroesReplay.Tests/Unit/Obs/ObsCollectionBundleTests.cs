using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.Obs;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsCollectionBundleTests
{
    private static readonly string[] RequiredScenes =
    {
        "game-scene",
        "waiting-screen",
        "prediction-report",
        "match-report",
        "request-queue",
    };

    private static readonly string[] RequiredSources =
    {
        "current-replay",
        "bronze-image",
        "silver-image",
        "gold-image",
        "platinum-image",
        "diamond-image",
        "master-image",
        "grandmaster-image",
        "prediction-report-browser",
        "match-report-browser",
        "request-queue-browser",
    };

    [Fact]
    public void Collection_HasNoCheckoutPathAndShipsNamedAssets()
    {
        string root = RepoRoot();
        string obsDirectory = Path.Combine(root, "obs");
        string collectionPath = Path.Combine(obsDirectory, "Default.json");
        string profilePath = Path.Combine(obsDirectory, "Default", "basic.ini");
        string json = File.ReadAllText(collectionPath);
        string profileBefore = File.ReadAllText(profilePath);

        Assert.DoesNotContain(
            @"C:\heroesreplay\HeroesReplay",
            json,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain(
            "C:/heroesreplay/HeroesReplay",
            json,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.False(ObsCollectionPaths.ContainsCheckoutPath(json));

        IReadOnlyList<string> assets = ObsCollectionPaths.PackageRelativePaths(obsDirectory, json);
        Assert.NotEmpty(assets);
        Assert.Contains("Ranks/bronze.png", assets, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("countdown/index.html", assets, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "Popups/settings-and-images/icon-sheet.png",
            assets,
            StringComparer.OrdinalIgnoreCase
        );
        Assert.DoesNotContain(
            assets,
            asset => asset.Contains("StormInterface", StringComparison.OrdinalIgnoreCase)
        );
        Assert.DoesNotContain(
            assets,
            asset => asset.EndsWith(".pdn", StringComparison.OrdinalIgnoreCase)
        );

        var expected = new HashSet<string>(assets, StringComparer.OrdinalIgnoreCase)
        {
            "Default.json",
            "Default/basic.ini",
        };
        IReadOnlyList<string> manifest = ReadManifest(obsDirectory);
        Assert.Equal(expected.Count, manifest.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (string packaged in manifest)
        {
            Assert.Contains(packaged, expected, StringComparer.OrdinalIgnoreCase);
            string full = Path.Combine(
                obsDirectory,
                packaged.Replace('/', Path.DirectorySeparatorChar)
            );
            Assert.True(File.Exists(full), full);
        }

        foreach (string asset in assets)
        {
            Assert.Contains(asset, manifest, StringComparer.OrdinalIgnoreCase);
        }

        Assert.Empty(ObsCollectionPaths.MissingAssets(obsDirectory, json));
        ObsCollectionInspection inspection = ObsCollectionPaths.Inspect(
            root,
            RequiredScenes,
            RequiredSources
        );
        Assert.True(inspection.Ok, inspection.Message);
        Assert.Equal(profileBefore, File.ReadAllText(profilePath));

        string rewritten = ObsCollectionPaths.Rewrite(
            json,
            @"C:\heroesreplay\app\obs",
            @"C:\heroesreplay\Data"
        );
        Assert.False(ObsCollectionPaths.ContainsCheckoutPath(rewritten));
        Assert.Contains(
            "C:/heroesreplay/app/obs/Ranks/bronze.png",
            rewritten.Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains("https://w.soundcloud.com/", rewritten, StringComparison.Ordinal);
        Assert.Contains(
            "file:///C:/heroesreplay/Data/queue.html",
            rewritten,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void Collection_HasNoDefaultMicrophoneAndKeepsDesktopAudio()
    {
        string json = File.ReadAllText(Path.Combine(RepoRoot(), "obs", "Default.json"));
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        // OBS keeps Mic/Aux as top-level AuxAudioDevice1..4. No entry is the same as "Disabled".
        foreach (JsonProperty property in root.EnumerateObject())
        {
            Assert.False(
                property.Name.StartsWith("AuxAudioDevice", StringComparison.Ordinal),
                property.Name
            );
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                Assert.False(
                    property.Value.TryGetProperty("id", out JsonElement id)
                        && id.GetString() == "wasapi_input_capture",
                    property.Name
                );
            }
        }

        Assert.DoesNotContain(
            ObsCollectionPaths.SourceNames(json),
            name => name.Equals("Mic/Aux", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Equal(
            "wasapi_output_capture",
            root.GetProperty("DesktopAudioDevice1").GetProperty("id").GetString()
        );
        Assert.Equal("HeroesReplay", root.GetProperty("name").GetString());
    }

    [Fact]
    public void Apply_LiveCollectionThatStillHasTheMicrophone_IsNotCustom()
    {
        string root = TempRoot();
        try
        {
            string template = WriteTemplate(root, "Ranks/bronze.png");
            string destination = Path.Combine(root, "live", "HeroesReplay.json");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            const string mic = """
                {
                  "AuxAudioDevice1": {
                    "name": "Mic/Aux",
                    "id": "wasapi_input_capture",
                    "settings": { "device_id": "default" },
                    "enabled": true,
                    "muted": false
                  },
                  "sources": [
                    {
                      "name": "bronze-image",
                      "id": "image_source",
                      "settings": { "file": "C:/heroesreplay/HeroesReplay/obs/Ranks/bronze.png" }
                    }
                  ]
                }
                """;
            File.WriteAllText(destination, mic);

            ObsCollectionApplyResult closed = Apply(
                root,
                template,
                destination,
                @"C:\heroesreplay\Data",
                obsIsRunning: false
            );

            Assert.False(closed.Drift, closed.Message);
            Assert.True(closed.Wrote);
            string updated = File.ReadAllText(destination);
            Assert.False(ObsCollectionPaths.ContainsCheckoutPath(updated));

            ObsCollectionApplyResult running = Apply(
                root,
                template,
                destination,
                @"C:\heroesreplay\Data",
                obsIsRunning: true
            );

            Assert.False(running.Drift, running.Message);
            Assert.False(running.Wrote);
            Assert.Equal(updated, File.ReadAllText(destination));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Apply_InstallsAMissingCollectionUnderTheConfiguredName()
    {
        string root = TempRoot();
        try
        {
            string template = WriteTemplate(root, "Ranks/bronze.png");
            string appData = Path.Combine(root, "appdata");
            string destination = ObsCollectionPatcher.LiveCollectionPath(
                appData,
                "HeroesReplay-dev"
            );

            ObsCollectionApplyResult result = Apply(
                root,
                template,
                destination,
                @"C:\heroesreplay\Data",
                obsIsRunning: false,
                collectionName: "HeroesReplay-dev"
            );

            Assert.True(result.Wrote);
            Assert.EndsWith(
                Path.Combine("scenes", "HeroesReplay-dev.json"),
                destination,
                StringComparison.OrdinalIgnoreCase
            );
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(destination));
            Assert.Equal("HeroesReplay-dev", document.RootElement.GetProperty("name").GetString());
            Assert.Contains(
                "/Ranks/bronze.png",
                document
                    .RootElement.GetProperty("sources")[0]
                    .GetProperty("settings")
                    .GetProperty("file")
                    .GetString(),
                StringComparison.Ordinal
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rewrite_UsesTheInstallAndDataDirectories()
    {
        const string json = """
            {
              "sources": [
                {
                  "name": "bronze-image",
                  "id": "image_source",
                  "settings": { "file": "Ranks/bronze.png" }
                },
                {
                  "name": "countdown",
                  "id": "browser_source",
                  "settings": { "url": "file:///countdown/index.html?m=2&s=0&autostart=1" }
                },
                {
                  "name": "queue",
                  "id": "browser_source",
                  "settings": { "url": "file:///C:/heroesreplay/Data/queue.html" }
                },
                {
                  "name": "legacy",
                  "id": "image_source",
                  "settings": { "file": "C:/heroesreplay/HeroesReplay/obs/hots-logo.png" }
                },
                {
                  "name": "soundcloud",
                  "id": "browser_source",
                  "settings": { "url": "https://w.soundcloud.com/player/?url=1" }
                }
              ]
            }
            """;

        string rewritten = ObsCollectionPaths.Rewrite(
            json,
            @"C:\heroesreplay\app\obs",
            @"D:\stream-data"
        );

        Assert.False(ObsCollectionPaths.ContainsCheckoutPath(rewritten));
        using JsonDocument document = JsonDocument.Parse(rewritten);
        var settings = document
            .RootElement.GetProperty("sources")
            .EnumerateArray()
            .ToDictionary(
                source => source.GetProperty("name").GetString(),
                source => source.GetProperty("settings")
            );

        Assert.Equal(
            "C:/heroesreplay/app/obs/Ranks/bronze.png",
            settings["bronze-image"].GetProperty("file").GetString()
        );
        Assert.Equal(
            "file:///C:/heroesreplay/app/obs/countdown/index.html?m=2&s=0&autostart=1",
            settings["countdown"].GetProperty("url").GetString()
        );
        Assert.Equal(
            "file:///D:/stream-data/queue.html",
            settings["queue"].GetProperty("url").GetString()
        );
        Assert.Equal(
            "C:/heroesreplay/app/obs/hots-logo.png",
            settings["legacy"].GetProperty("file").GetString()
        );
        Assert.Equal(
            "https://w.soundcloud.com/player/?url=1",
            settings["soundcloud"].GetProperty("url").GetString()
        );
    }

    [Fact]
    public void MissingAssets_ReportsAFileThatIsNotInTheTree()
    {
        string root = TempRoot();
        try
        {
            const string json = """
                { "sources": [ { "name": "bronze-image", "settings": { "file": "Ranks/bronze.png" } } ] }
                """;

            IReadOnlyList<string> missing = ObsCollectionPaths.MissingAssets(root, json);

            Assert.Equal(new[] { "Ranks/bronze.png" }, missing);
            Assert.Contains(
                "game-scene",
                ObsCollectionPaths.MissingScenes(json, new[] { "game-scene" })
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Apply_SkipsWhileObsIsRunning()
    {
        string root = TempRoot();
        try
        {
            string template = WriteTemplate(root, "Ranks/bronze.png");
            string destination = Path.Combine(root, "live", "HeroesReplay.json");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.WriteAllText(destination, File.ReadAllText(template));
            string before = File.ReadAllText(destination);

            ObsCollectionApplyResult result = Apply(
                root,
                template,
                destination,
                @"C:\heroesreplay\Data",
                obsIsRunning: true
            );

            Assert.False(result.Wrote);
            Assert.False(result.Drift);
            Assert.True(result.Deferred);
            Assert.Contains("OBS is running", result.Message, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllText(destination));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Apply_ReportsDriftForACustomCollection()
    {
        string root = TempRoot();
        try
        {
            string template = WriteTemplate(root, "Ranks/bronze.png");
            string destination = Path.Combine(root, "live", "HeroesReplay.json");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            const string custom = """
                { "sources": [ { "name": "my-overlay", "id": "image_source", "settings": { "file": "C:/heroesreplay/HeroesReplay/obs/Ranks/bronze.png" } } ] }
                """;
            File.WriteAllText(destination, custom);

            ObsCollectionApplyResult result = Apply(
                root,
                template,
                destination,
                @"C:\heroesreplay\Data",
                obsIsRunning: false
            );

            Assert.False(result.Wrote);
            Assert.True(result.Drift);
            Assert.Contains("not overwritten", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(custom, File.ReadAllText(destination));
            Assert.Contains(
                "HeroesReplay",
                File.ReadAllText(destination),
                StringComparison.Ordinal
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Apply_UpdatesPathsWhenObsIsClosed()
    {
        string root = TempRoot();
        try
        {
            string template = WriteTemplate(root, "Ranks/bronze.png");
            string destination = Path.Combine(root, "live", "HeroesReplay.json");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string templateBefore = File.ReadAllText(template);
            File.WriteAllText(
                destination,
                templateBefore.Replace(
                    "Ranks/bronze.png",
                    "C:/heroesreplay/HeroesReplay/obs/Ranks/bronze.png"
                )
            );

            ObsCollectionApplyResult result = Apply(
                root,
                template,
                destination,
                @"D:\stream-data",
                obsIsRunning: false
            );

            string updated = File.ReadAllText(destination);
            Assert.True(result.Wrote);
            Assert.False(result.Drift);
            Assert.False(ObsCollectionPaths.ContainsCheckoutPath(updated));
            Assert.Contains(
                Path.GetFullPath(Path.Combine(root, "obs")).Replace('\\', '/')
                    + "/Ranks/bronze.png",
                updated.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase
            );
            Assert.Equal(templateBefore, File.ReadAllText(template));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Apply_DoesNotTouchTheProfileOrStreamKey()
    {
        string root = TempRoot();
        try
        {
            string template = WriteTemplate(root, "Ranks/bronze.png");
            string ini = Path.Combine(root, "obs", "Default", "basic.ini");
            string service = Path.Combine(root, "obs", "Default", "service.json");
            Directory.CreateDirectory(Path.GetDirectoryName(ini));
            const string profile = "[Stream]\r\nstream_key=unit-test-not-a-secret\r\n";
            const string streamService = "{\"key\":\"unit-test-not-a-secret\"}";
            File.WriteAllText(ini, profile);
            File.WriteAllText(service, streamService);
            string destination = Path.Combine(root, "live", "HeroesReplay.json");

            ObsCollectionApplyResult sameFile = Apply(
                root,
                template,
                template,
                @"C:\heroesreplay\Data",
                obsIsRunning: false
            );
            ObsCollectionApplyResult result = Apply(
                root,
                template,
                destination,
                @"C:\heroesreplay\Data",
                obsIsRunning: false
            );

            Assert.True(sameFile.Drift);
            Assert.False(sameFile.Wrote);
            Assert.Contains(
                "Ranks/bronze.png",
                File.ReadAllText(template),
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(
                "C:/heroesreplay/HeroesReplay",
                File.ReadAllText(template),
                StringComparison.OrdinalIgnoreCase
            );
            Assert.True(result.Wrote);
            Assert.Equal(profile, File.ReadAllText(ini));
            Assert.Equal(streamService, File.ReadAllText(service));
            Assert.False(File.Exists(Path.Combine(root, "obs", "Default", "service.json.bak")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Apply_TemplateChange_ReplacesTheManagedCollectionAfterABackup()
    {
        string root = TempRoot();
        try
        {
            string template = WriteTemplate(root, "Ranks/bronze.png");
            string destination = Path.Combine(root, "live", "HeroesReplay.json");
            Assert.True(Apply(root, template, destination, @"C:\heroesreplay\Data", false).Wrote);
            string installed = File.ReadAllText(destination);

            // A new build changes a setting of a managed source; the names stay the same.
            File.WriteAllText(
                template,
                File.ReadAllText(template).Replace("\"file\"", "\"unload\": true, \"file\"")
            );
            ObsCollectionApplyResult result = Apply(
                root,
                template,
                destination,
                @"C:\heroesreplay\Data",
                obsIsRunning: false
            );

            Assert.True(result.Wrote, result.Message);
            Assert.Contains("\"unload\": true", File.ReadAllText(destination));
            Assert.Equal(installed, File.ReadAllText(result.Backup));
            Assert.Contains(result.Backup, result.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Apply_OperatorAddedAScene_KeepsTheCollectionEvenWhenTheTemplateChanges()
    {
        string root = TempRoot();
        try
        {
            string template = WriteTemplate(root, "Ranks/bronze.png");
            string destination = Path.Combine(root, "live", "HeroesReplay.json");
            Apply(root, template, destination, @"C:\heroesreplay\Data", false);
            string custom = File.ReadAllText(destination)
                .Replace(
                    "\"sources\": [",
                    "\"sources\": [ { \"name\": \"intermission\", \"id\": \"scene\", \"settings\": {} },"
                );
            File.WriteAllText(destination, custom);
            File.WriteAllText(
                template,
                File.ReadAllText(template).Replace("\"file\"", "\"unload\": true, \"file\"")
            );

            ObsCollectionApplyResult result = Apply(
                root,
                template,
                destination,
                @"C:\heroesreplay\Data",
                obsIsRunning: false,
                release: true
            );

            Assert.True(result.Drift);
            Assert.False(result.Wrote);
            Assert.Contains("intermission", result.Message, StringComparison.Ordinal);
            Assert.Equal(custom, File.ReadAllText(destination));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ObsCollectionApplyResult Apply(
        string root,
        string template,
        string destination,
        string dataDirectory,
        bool obsIsRunning,
        string collectionName = null,
        bool release = false,
        string previousTemplate = null
    ) =>
        ObsCollectionPatcher.Apply(
            new ObsCollectionUpdate
            {
                TemplatePath = template,
                DestinationPath = destination,
                DataDirectory = dataDirectory,
                ObsIsRunning = obsIsRunning,
                CollectionName = collectionName,
                Managed = Managed(root),
                Release = release,
                PreviousTemplatePath = previousTemplate,
            }
        );

    private static ObsManagedFiles Managed(string root) => new(Path.Combine(root, "managed"));

    private static string WriteTemplate(string root, string relativeFile)
    {
        string template = Path.Combine(root, "obs", "Default.json");
        Directory.CreateDirectory(Path.GetDirectoryName(template));
        File.WriteAllText(
            template,
            """
            {
              "sources": [
                {
                  "name": "bronze-image",
                  "id": "image_source",
                  "settings": { "file": "RELATIVE" }
                }
              ]
            }
            """.Replace("RELATIVE", relativeFile)
        );
        return template;
    }

    private static string TempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-obs-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }

    private static IReadOnlyList<string> ReadManifest(string obsDirectory)
    {
        return File.ReadAllLines(Path.Combine(obsDirectory, "bundle.manifest"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .ToList();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "heroes-replay.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("heroes-replay.slnx");
    }
}
