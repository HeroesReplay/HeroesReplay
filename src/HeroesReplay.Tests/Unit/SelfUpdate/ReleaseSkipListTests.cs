using System;
using System.IO;
using HeroesReplay.CLI.Commands.Update;
using HeroesReplay.Core.SelfUpdate;
using Xunit;

namespace HeroesReplay.Tests.Unit.SelfUpdate;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReleaseSkipListTests
{
    private const string Latest = """
        {
          "tag_name": "v1.4.0",
          "assets": [
            { "name": "heroesreplay-win-x64.zip", "browser_download_url": "https://example.test/app.zip" }
          ]
        }
        """;

    [Fact]
    public void Parse_ReadsTheTagOfEachLineTheScriptWrites()
    {
        // apply-release.ps1 Add-SkippedRelease: tag, tab, UTC time, tab, reason.
        ReleaseSkipList list = ReleaseSkipList.Parse(
            "# rolled back here\r\n"
                + "v1.4.0\t2026-10-02T12:20:00.0000000Z\trelease-health exit 2. unhealthy: spectate has shown no match progress since the install.\r\n"
                + "\r\n"
                + "  v1.3.9 \n"
        );

        Assert.True(list.Contains("v1.4.0"));
        Assert.True(list.Contains("V1.4.0 "));
        Assert.True(list.Contains("v1.3.9"));
        Assert.False(list.Contains("v1.4.1"));
        Assert.False(list.Contains("# rolled"));
        Assert.False(list.Contains(""));
        Assert.Equal(2, list.Tags.Count);
    }

    [Fact]
    public void Load_AMissingFileSkipsNothing()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-skip-" + Path.GetRandomFileName());
        try
        {
            Assert.Empty(ReleaseSkipList.Load(Path.Combine(root, "missing.txt")).Tags);

            Directory.CreateDirectory(root);
            string path = Path.Combine(root, ReleaseSkipList.FileName);
            File.WriteAllText(path, "v1.4.0\t2026-10-02T12:20:00Z\tunhealthy\r\n");
            Assert.True(ReleaseSkipList.Load(path).Contains("v1.4.0"));
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
    public void Gate_DoesNotStageASkippedTag()
    {
        ReleaseOffer? offer = GitHubReleaseJson.Read(
            Latest,
            ReleaseSettings.DefaultAssetName,
            "v1.3.9"
        );

        ReleaseOffer? skipped = ReleaseUpdateGate.Pick(
            offer,
            ReleaseSkipList.Parse("v1.4.0\t2026-10-02T12:20:00Z\tunhealthy"),
            out string skippedTag
        );
        ReleaseOffer? allowed = ReleaseUpdateGate.Pick(
            offer,
            ReleaseSkipList.Parse("v1.3.8"),
            out string none
        );

        Assert.Null(skipped);
        Assert.Equal("v1.4.0", skippedTag);
        Assert.Equal("v1.4.0", allowed?.Version);
        Assert.Null(none);
        Assert.Null(ReleaseUpdateGate.Pick(null, ReleaseSkipList.Parse("v1.4.0"), out _));
    }

    [Fact]
    public void Gate_TellsTheHelperTheVersionAndWhetherTheStackWasSupervised()
    {
        string supervised = ReleaseUpdateGate.HelperArguments(
            @"C:\heroesreplay\app\apply-release.ps1",
            @"C:\heroesreplay\app",
            @"C:\updates\v1.4.0\prepared",
            4242,
            "v1.4.0",
            supervised: true
        );
        string plain = ReleaseUpdateGate.HelperArguments(
            "a.ps1",
            "app",
            "prepared",
            7,
            "v1.4.0",
            supervised: false
        );

        Assert.Contains("-WaitForPid 4242 -Version \"v1.4.0\" -Supervise", supervised);
        Assert.Contains("-InstallDir \"C:\\heroesreplay\\app\"", supervised);
        Assert.EndsWith("-Version \"v1.4.0\"", plain);
        Assert.DoesNotContain("-Supervise", plain, StringComparison.Ordinal);
    }
}
