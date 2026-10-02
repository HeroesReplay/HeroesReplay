using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using HeroesReplay.CLI.Commands;
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
    public void Swap_ReplacesTheInstallAndLeavesTheQueueAlone()
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
            ReleaseInstall.Swap(install, prepared);

            Assert.Equal("new", File.ReadAllText(Path.Combine(install, "heroesreplay.exe")));
            Assert.Equal(
                "secret",
                File.ReadAllText(Path.Combine(install, "appsettings.secrets.json"))
            );
            Assert.False(File.Exists(Path.Combine(install, "obs", "Default", "service.json")));
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
    public void MayDiscardPrevious_WaitsUntilTheStackHasBeenHealthy()
    {
        Assert.False(ReleaseHealth.MayDiscardPrevious(false, ReleaseHealth.StabilizeFor));
        Assert.False(ReleaseHealth.MayDiscardPrevious(true, TimeSpan.FromMinutes(-1)));
        Assert.False(
            ReleaseHealth.MayDiscardPrevious(
                true,
                ReleaseHealth.StabilizeFor.Subtract(TimeSpan.FromTicks(1))
            )
        );
        Assert.True(ReleaseHealth.MayDiscardPrevious(true, ReleaseHealth.StabilizeFor));
    }

    [Fact]
    public void MayDiscardRoleFile_WaitsForReadyRolesInsteadOfFolderAge()
    {
        DateTimeOffset since = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        string text = ReleaseHealth.FormatRoleFile(since);

        Assert.False(ReleaseHealth.MayDiscardRoleFile(null, since.AddMinutes(3)));
        Assert.False(ReleaseHealth.MayDiscardRoleFile("", since.AddMinutes(3)));
        Assert.False(ReleaseHealth.MayDiscardRoleFile("roles=ready", since.AddMinutes(3)));
        Assert.False(ReleaseHealth.MayDiscardRoleFile(text, since.AddMinutes(1)));
        Assert.True(ReleaseHealth.MayDiscardRoleFile(text, since.Add(ReleaseHealth.StabilizeFor)));
    }

    [Fact]
    public async Task ReleaseHealthCommand_RefusesAMissingRoleFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-role-cmd-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "role-ready.txt");
        try
        {
            int missing = await new HeroesReplayCommand()
                .Parse("update release-health --role-file " + path)
                .InvokeAsync();
            File.WriteAllText(path, ReleaseHealth.FormatRoleFile(DateTimeOffset.UtcNow));
            int early = await new HeroesReplayCommand()
                .Parse("update release-health --role-file " + path)
                .InvokeAsync();
            File.WriteAllText(
                path,
                ReleaseHealth.FormatRoleFile(
                    DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(3))
                )
            );
            int ready = await new HeroesReplayCommand()
                .Parse("update release-health --role-file " + path)
                .InvokeAsync();

            Assert.Equal(1, missing);
            Assert.Equal(1, early);
            Assert.Equal(0, ready);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void Swap_KeepsThePreviousInstallInsideTheStabilizationWindow()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-window-" + Path.GetRandomFileName());
        string install = Path.Combine(root, "app");
        string staged = Path.Combine(root, "staged");
        string previous = install + ".previous";
        try
        {
            Directory.CreateDirectory(install);
            Directory.CreateDirectory(staged);
            Directory.CreateDirectory(previous);
            File.WriteAllText(Path.Combine(install, "heroesreplay.exe"), "old");
            File.WriteAllText(Path.Combine(staged, "heroesreplay.exe"), "new");
            File.WriteAllText(Path.Combine(previous, "marker.txt"), "keep");

            Assert.Throws<InvalidOperationException>(() => ReleaseInstall.Swap(install, staged));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(previous, "marker.txt")));
            Assert.Equal("old", File.ReadAllText(Path.Combine(install, "heroesreplay.exe")));

            ReleaseInstall.Swap(install, staged, healthyFor: ReleaseHealth.StabilizeFor);

            Assert.Equal("new", File.ReadAllText(Path.Combine(install, "heroesreplay.exe")));
            Assert.Equal("old", File.ReadAllText(Path.Combine(previous, "heroesreplay.exe")));
            Assert.False(File.Exists(Path.Combine(previous, "marker.txt")));
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
    public void CopyObsScenes_SkipsWhenObsIsRunning()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-obs-" + Path.GetRandomFileName());
        string install = Path.Combine(root, "app");
        string appData = Path.Combine(root, "appdata");
        try
        {
            Directory.CreateDirectory(Path.Combine(install, "obs"));
            File.WriteAllText(Path.Combine(install, "obs", "Default.json"), "{\"scenes\":[]}");

            IReadOnlyList<string> open = ReleaseInstall.CopyObsScenesIfClosed(
                install,
                appData,
                obsIsRunning: true
            );

            Assert.Contains(open, note => note.Contains("OBS is open", StringComparison.Ordinal));
            Assert.False(
                File.Exists(
                    Path.Combine(appData, "obs-studio", "basic", "scenes", "HeroesReplay.json")
                )
            );

            ReleaseInstall.CopyObsScenesIfClosed(install, appData, obsIsRunning: false);

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
    public void CopyObsScenes_KeepsAnExistingProfileAndNeverCopiesTheStreamKey()
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

            IReadOnlyList<string> notes = ReleaseInstall.CopyObsScenesIfClosed(
                install,
                appData,
                obsIsRunning: false
            );

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
    public void CopyObsScenes_InstallsTheProfileTemplateOnlyWhenMissing()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-obs-" + Path.GetRandomFileName());
        string install = Path.Combine(root, "app");
        string appData = Path.Combine(root, "appdata");
        try
        {
            WriteObsBundle(install);

            IReadOnlyList<string> notes = ReleaseInstall.CopyObsScenesIfClosed(
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
            ReleaseInstall.CopyObsScenesIfClosed(
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
