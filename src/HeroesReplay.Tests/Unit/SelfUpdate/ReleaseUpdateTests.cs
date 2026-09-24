using System.IO;
using HeroesReplay.Core.Services.SelfUpdate;
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
            Assert.Equal("secret", File.ReadAllText(Path.Combine(install, "appsettings.secrets.json")));
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
    public void CopyObsScenes_SkipsWhenObsIsRunning()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-obs-" + Path.GetRandomFileName());
        string install = Path.Combine(root, "app");
        string appData = Path.Combine(root, "appdata");
        try
        {
            Directory.CreateDirectory(Path.Combine(install, "obs"));
            File.WriteAllText(Path.Combine(install, "obs", "Default.json"), "{\"scenes\":[]}");

            ReleaseInstall.CopyObsScenesIfClosed(install, appData, obsIsRunning: true);

            Assert.False(
                File.Exists(Path.Combine(appData, "obs-studio", "basic", "scenes", "HeroesReplay.json"))
            );

            ReleaseInstall.CopyObsScenesIfClosed(install, appData, obsIsRunning: false);

            Assert.Equal(
                "{\"scenes\":[]}",
                File.ReadAllText(Path.Combine(appData, "obs-studio", "basic", "scenes", "HeroesReplay.json"))
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
    public void LooksLikeSourceBuild_RefusesTheWorktree()
    {
        Assert.True(ReleaseInstall.LooksLikeSourceBuild(@"C:\heroesreplay\worktrees\develop\src\HeroesReplay.CLI\bin"));
        Assert.False(ReleaseInstall.LooksLikeSourceBuild(@"C:\heroesreplay\app"));
    }
}
