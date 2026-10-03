using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.SelfUpdate;
using Xunit;

namespace HeroesReplay.Tests.Unit.SelfUpdate;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReleaseUpdateTests
{
    [Fact]
    public void Read_SameVersion_DoesNotOfferAnUpdate()
    {
        const string json = """
            {
              "tag_name": "abc123",
              "assets": [
                { "name": "heroesreplay-win-x64.zip", "browser_download_url": "https://example.test/app.zip" }
              ]
            }
            """;

        Assert.Null(GitHubReleaseJson.Read(json, "heroesreplay-win-x64.zip", "abc123"));
    }

    [Fact]
    public void Read_NewVersion_ReturnsTheZipUrl()
    {
        const string json = """
            {
              "tag_name": "def456",
              "assets": [
                { "name": "notes.txt", "browser_download_url": "https://example.test/notes.txt" },
                { "name": "heroesreplay-win-x64.zip", "browser_download_url": "https://example.test/app.zip" }
              ]
            }
            """;

        ReleaseOffer? offer = GitHubReleaseJson.Read(json, "heroesreplay-win-x64.zip", "abc123");

        Assert.Equal("def456", offer?.Version);
        Assert.Equal("https://example.test/app.zip", offer?.DownloadUrl);
    }

    [Fact]
    public void CopyPublish_PreparesTheReleaseAndLeavesTheQueueAlone()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-release-" + Path.GetRandomFileName());
        string install = Path.Combine(root, "app");
        string staged = Path.Combine(root, "staged");
        string data = Path.Combine(root, "Data");
        string secrets = Path.Combine(root, "secrets", "appsettings.secrets.json");
        try
        {
            Directory.CreateDirectory(install);
            Directory.CreateDirectory(staged);
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(Path.GetDirectoryName(secrets)!);
            File.WriteAllText(Path.Combine(install, "heroesreplay.exe"), "old");
            File.WriteAllText(Path.Combine(install, "version.txt"), "old");
            File.WriteAllText(Path.Combine(staged, "heroesreplay.exe"), "new");
            File.WriteAllText(Path.Combine(staged, "version.txt"), "new");
            Directory.CreateDirectory(Path.Combine(staged, "obs", "Default"));
            File.WriteAllText(Path.Combine(staged, "obs", "Default", "service.json"), "key");
            File.WriteAllText(Path.Combine(staged, "obs", "Default.json"), "{}");
            File.WriteAllText(secrets, "secret");
            File.WriteAllText(Path.Combine(data, "spectated-ids.txt"), "65268119");
            File.WriteAllText(Path.Combine(data, "requests.json"), "[]");

            string prepared = Path.Combine(root, "prepared");
            ReleaseInstall.CopyPublish(staged, prepared);
            ReleaseInstall.PreserveSecrets(secrets, install, prepared);

            Assert.Equal("new", File.ReadAllText(Path.Combine(prepared, "heroesreplay.exe")));
            Assert.Equal(
                "secret",
                File.ReadAllText(Path.Combine(prepared, "appsettings.secrets.json"))
            );
            Assert.False(File.Exists(Path.Combine(prepared, "obs", "Default", "service.json")));
            Assert.Equal("old", File.ReadAllText(Path.Combine(install, "heroesreplay.exe")));
            Assert.Equal("65268119", File.ReadAllText(Path.Combine(data, "spectated-ids.txt")));
            Assert.Equal("[]", File.ReadAllText(Path.Combine(data, "requests.json")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void InstallObsFiles_SkipsWhenObsIsRunning()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-obs-" + Path.GetRandomFileName());
        string install = Path.Combine(root, "app");
        string appData = Path.Combine(root, "appdata");
        try
        {
            Directory.CreateDirectory(Path.Combine(install, "obs"));
            File.WriteAllText(Path.Combine(install, "obs", "Default.json"), "{\"scenes\":[]}");

            IReadOnlyList<string> open = InstallObs(install, appData, obsIsRunning: true);

            Assert.Contains(
                open,
                note => note.Contains("OBS is running", StringComparison.Ordinal)
            );
            Assert.False(
                File.Exists(
                    Path.Combine(appData, "obs-studio", "basic", "scenes", "HeroesReplay.json")
                )
            );

            InstallObs(install, appData, obsIsRunning: false);

            Assert.Equal(
                "{\"scenes\":[]}",
                File.ReadAllText(
                    Path.Combine(appData, "obs-studio", "basic", "scenes", "HeroesReplay.json")
                )
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void InstallObsFiles_KeepsAnExistingProfileAndNeverCopiesTheStreamKey()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-obs-" + Path.GetRandomFileName());
        string install = Path.Combine(root, "app");
        string appData = Path.Combine(root, "appdata");
        try
        {
            WriteObsBundle(install);
            File.WriteAllText(
                Path.Combine(install, "obs", "Default", "service.json"),
                "{\"key\":\"unit-test-not-a-secret\"}"
            );
            string profileDir = Path.Combine(
                appData,
                "obs-studio",
                "basic",
                "profiles",
                "HeroesReplay"
            );
            Directory.CreateDirectory(profileDir);
            const string machine =
                "[General]\r\nName=HeroesReplay\r\n\r\n[SimpleOutput]\r\nRecEncoder=nvenc\r\n";
            File.WriteAllText(Path.Combine(profileDir, "basic.ini"), machine);

            IReadOnlyList<string> notes = InstallObs(install, appData, obsIsRunning: false);

            Assert.Equal(machine, File.ReadAllText(Path.Combine(profileDir, "basic.ini")));
            Assert.Contains(
                notes,
                note => note.Contains("Kept the existing OBS profile", StringComparison.Ordinal)
            );
            Assert.False(File.Exists(Path.Combine(profileDir, "service.json")));
            Assert.Empty(Directory.GetFiles(appData, "service.json", SearchOption.AllDirectories));
            Assert.True(
                File.Exists(
                    Path.Combine(appData, "obs-studio", "basic", "scenes", "HeroesReplay.json")
                )
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void InstallObsFiles_InstallsTheProfileTemplateOnlyWhenMissing()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-obs-" + Path.GetRandomFileName());
        string install = Path.Combine(root, "app");
        string appData = Path.Combine(root, "appdata");
        try
        {
            WriteObsBundle(install);

            IReadOnlyList<string> notes = InstallObs(
                install,
                appData,
                obsIsRunning: false,
                profileName: "HeroesReplay-live",
                collectionName: "HeroesReplay-live"
            );

            string profile = Path.Combine(
                appData,
                "obs-studio",
                "basic",
                "profiles",
                "HeroesReplay-live",
                "basic.ini"
            );
            Assert.Contains("Name=HeroesReplay-live\r\n", File.ReadAllText(profile));
            Assert.Contains("RecEncoder=qsv_h264", File.ReadAllText(profile));
            Assert.Contains(notes, note => note.Contains("template", StringComparison.Ordinal));
            string collection = Path.Combine(
                appData,
                "obs-studio",
                "basic",
                "scenes",
                "HeroesReplay-live.json"
            );
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(collection)))
            {
                Assert.Equal(
                    "HeroesReplay-live",
                    document.RootElement.GetProperty("name").GetString()
                );
            }

            File.WriteAllText(profile, "[General]\r\nName=HeroesReplay-live\r\n; tuned\r\n");
            InstallObs(
                install,
                appData,
                obsIsRunning: false,
                profileName: "HeroesReplay-live",
                collectionName: "HeroesReplay-live"
            );

            Assert.Equal(
                "[General]\r\nName=HeroesReplay-live\r\n; tuned\r\n",
                File.ReadAllText(profile)
            );
            Assert.False(
                Directory.Exists(
                    Path.Combine(appData, "obs-studio", "basic", "profiles", "HeroesReplay")
                )
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void InstallObsFiles_ReplacesTheManagedCollectionAfterABackup()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-obs-" + Path.GetRandomFileName());
        string install = Path.Combine(root, "app");
        string previous = Path.Combine(root, "app.previous");
        string appData = Path.Combine(root, "appdata");
        try
        {
            // The new release adds a source; the live collection is the previous release's.
            WriteTemplate(previous, "rank-image");
            WriteTemplate(install, "rank-image", "queue-browser");
            string live = Path.Combine(
                appData,
                "obs-studio",
                "basic",
                "scenes",
                "HeroesReplay.json"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(live)!);
            string before = "{\"current_scene\":\"x\"," + Collection("rank-image").Substring(1);
            File.WriteAllText(live, before);

            IReadOnlyList<string> notes = InstallObs(
                install,
                appData,
                obsIsRunning: false,
                previousInstall: previous
            );

            Assert.Contains("queue-browser", File.ReadAllText(live));
            string backup = Assert.Single(
                ObsFileTransaction.Backups(Managed(appData).BackupDirectory, live)
            );
            Assert.Equal(before, File.ReadAllText(backup));
            Assert.Contains(notes, note => note.Contains(backup, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InstallObsFiles_NeverOverwritesACustomCollection()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-obs-" + Path.GetRandomFileName());
        string install = Path.Combine(root, "app");
        string previous = Path.Combine(root, "app.previous");
        string appData = Path.Combine(root, "appdata");
        try
        {
            WriteTemplate(previous, "rank-image");
            WriteTemplate(install, "rank-image", "queue-browser");
            string live = Path.Combine(
                appData,
                "obs-studio",
                "basic",
                "scenes",
                "HeroesReplay.json"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(live)!);
            string custom = Collection("rank-image", "my-webcam");
            File.WriteAllText(live, custom);

            IReadOnlyList<string> notes = InstallObs(
                install,
                appData,
                obsIsRunning: false,
                previousInstall: previous
            );

            Assert.Equal(custom, File.ReadAllText(live));
            Assert.Contains(
                notes,
                note =>
                    note.Contains("not overwritten", StringComparison.Ordinal)
                    && note.Contains("my-webcam", StringComparison.Ordinal)
            );
            Assert.Empty(ObsFileTransaction.Backups(Managed(appData).BackupDirectory, live));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InstallObsFiles_WhileObsRuns_ReplacesTheCollectionOnceObsIsClosed()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-obs-" + Path.GetRandomFileName());
        string install = Path.Combine(root, "app");
        string previous = Path.Combine(root, "app.previous");
        string appData = Path.Combine(root, "appdata");
        try
        {
            WriteTemplate(previous, "rank-image");
            WriteTemplate(install, "rank-image", "queue-browser");
            string live = Path.Combine(
                appData,
                "obs-studio",
                "basic",
                "scenes",
                "HeroesReplay.json"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(live)!);
            string before = Collection("rank-image");
            File.WriteAllText(live, before);

            IReadOnlyList<string> notes = InstallObs(
                install,
                appData,
                obsIsRunning: true,
                previousInstall: previous
            );

            Assert.Equal(before, File.ReadAllText(live));
            Assert.Contains(
                notes,
                note => note.Contains("OBS is running", StringComparison.Ordinal)
            );

            // services start (no release, no previous install) with OBS closed.
            ObsCollectionApplyResult start = ObsCollectionPatcher.Apply(
                new ObsCollectionUpdate
                {
                    TemplatePath = Path.Combine(install, "obs", "Default.json"),
                    DestinationPath = live,
                    Managed = Managed(appData),
                }
            );

            Assert.True(start.Wrote, start.Message);
            Assert.Contains("queue-browser", File.ReadAllText(live));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InstallObsSettings_UseTheEnvironmentOverlay()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-obs-" + Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(
                Path.Combine(root, "appsettings.json"),
                """{ "Location": { "DataDirectory": "C:\\heroesreplay\\Data" }, "OBS": { "SceneCollectionName": "HeroesReplay" } }"""
            );
            File.WriteAllText(
                Path.Combine(root, "appsettings.dev.json"),
                """{ "Location": { "DataDirectory": "D:\\dev-data" }, "OBS": { "SceneCollectionName": "HeroesReplay-dev" } }"""
            );

            (OBSSettings dev, string devData) =
                HeroesReplay.CLI.ServiceCollectionExtensions.LoadInstallObsSettings(root, "dev");
            (OBSSettings prod, string prodData) =
                HeroesReplay.CLI.ServiceCollectionExtensions.LoadInstallObsSettings(root, "prod");

            Assert.Equal(@"D:\dev-data", devData);
            Assert.Equal("HeroesReplay-dev", ObsNames.SceneCollection(dev));
            Assert.Equal(@"C:\heroesreplay\Data", prodData);
            Assert.Equal("HeroesReplay", ObsNames.SceneCollection(prod));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static IReadOnlyList<string> InstallObs(
        string install,
        string appData,
        bool obsIsRunning,
        string profileName = null,
        string collectionName = null,
        string previousInstall = null
    ) =>
        ReleaseInstall.InstallObsFiles(
            new ReleaseObsInstall
            {
                InstallDirectory = install,
                AppData = appData,
                ObsIsRunning = obsIsRunning,
                Managed = Managed(appData),
                ProfileName = profileName,
                CollectionName = collectionName,
                PreviousInstall = previousInstall,
            }
        );

    private static ObsManagedFiles Managed(string appData) =>
        new(Path.Combine(Path.GetDirectoryName(appData)!, "managed"));

    private static void WriteTemplate(string install, params string[] sources)
    {
        Directory.CreateDirectory(Path.Combine(install, "obs"));
        File.WriteAllText(Path.Combine(install, "obs", "Default.json"), Collection(sources));
    }

    private static string Collection(params string[] sources) =>
        "{\"name\":\"HeroesReplay\",\"sources\":["
        + string.Join(
            ",",
            Array.ConvertAll(
                sources,
                name => "{\"name\":\"" + name + "\",\"id\":\"image_source\",\"settings\":{}}"
            )
        )
        + "]}";

    private static void WriteObsBundle(string install)
    {
        Directory.CreateDirectory(Path.Combine(install, "obs", "Default"));
        File.WriteAllText(
            Path.Combine(install, "obs", "Default.json"),
            "{\"name\":\"HeroesReplay\",\"sources\":[]}"
        );
        File.WriteAllText(
            Path.Combine(install, "obs", "Default", "basic.ini"),
            "[General]\r\nName=HeroesReplay\r\n\r\n[SimpleOutput]\r\nRecEncoder=qsv_h264\r\n"
        );
    }

    [Fact]
    public void LooksLikeSourceBuild_RefusesTheWorktree()
    {
        Assert.True(
            ReleaseInstall.LooksLikeSourceBuild(
                @"C:\heroesreplay\worktrees\develop\src\HeroesReplay.CLI\bin"
            )
        );
        Assert.False(ReleaseInstall.LooksLikeSourceBuild(@"C:\heroesreplay\app"));
    }
}
