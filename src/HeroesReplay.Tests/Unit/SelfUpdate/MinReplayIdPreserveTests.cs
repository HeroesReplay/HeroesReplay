using System.CommandLine;
using System.IO;
using HeroesReplay.CLI.Commands;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.SelfUpdate;
using Xunit;

namespace HeroesReplay.Tests.Unit.SelfUpdate;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MinReplayIdPreserveTests
{
    private const string ZipSettings = """
        {
          "HeroesProfileApi": {
            "MinReplayId": 65267450,
            "CachedReplayLimit": 9
          }
        }
        """;

    [Fact]
    public void TryPreserveHigher_ReplacesTheZipDefaultWhenTheMachineIdIsHigher()
    {
        const string previous = """
            { "HeroesProfileApi": { "MinReplayId": 65536853, "CachedReplayLimit": 5 } }
            """;

        Assert.True(MinReplayIdFile.TryPreserveHigher(ZipSettings, previous, out string updated));
        Assert.Contains("\"MinReplayId\": 65536853", updated);
        Assert.DoesNotContain("65267450", updated);
        Assert.Contains("\"CachedReplayLimit\": 9", updated);
        Assert.True(
            MinReplayIdFile.TryPreserveHigher(
                ZipSettings,
                "{ \"MinReplayId\" : 65536853 }",
                out string spaced
            )
        );
        Assert.Contains("\"MinReplayId\": 65536853", spaced);
    }

    [Fact]
    public void TryPreserveHigher_LeavesTheZipDefaultWhenThePreviousFileHasNoId()
    {
        const string previous = """
            { "HeroesProfileApi": { "CachedReplayLimit": 5 } }
            """;

        Assert.False(MinReplayIdFile.TryPreserveHigher(ZipSettings, previous, out string updated));
        Assert.Equal(ZipSettings, updated);
        Assert.False(MinReplayIdFile.TryPreserveHigher(ZipSettings, "", out string emptyPrevious));
        Assert.Equal(ZipSettings, emptyPrevious);
    }

    [Fact]
    public void TryPreserveHigher_LeavesAHigherZipFloorInPlace()
    {
        const string previous = """
            { "HeroesProfileApi": { "MinReplayId": 100 } }
            """;

        Assert.False(MinReplayIdFile.TryPreserveHigher(ZipSettings, previous, out string updated));
        Assert.Contains("\"MinReplayId\": 65267450", updated);
    }

    [Fact]
    public void TryPreserveHigher_LeavesTheZipWhenTheIdsMatch()
    {
        const string previous = """
            { "HeroesProfileApi": { "MinReplayId": 65267450, "CachedReplayLimit": 5 } }
            """;

        Assert.False(MinReplayIdFile.TryPreserveHigher(ZipSettings, previous, out string updated));
        Assert.Equal(ZipSettings, updated);
    }

    [Fact]
    public void PreserveMinReplayId_WritesTheHigherIdAndLeavesTheRestOfTheZip()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-min-replay-" + Path.GetRandomFileName());
        string install = Path.Combine(root, "app", "appsettings.json");
        string staged = Path.Combine(root, "prepared", "appsettings.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(install)!);
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.WriteAllText(
                install,
                "{ \"HeroesProfileApi\": { \"MinReplayId\": 65536853, \"Old\": true } }"
            );
            File.WriteAllText(staged, ZipSettings);

            ReleaseInstall.PreserveMinReplayId(install, staged);

            string updated = File.ReadAllText(staged);
            Assert.Contains("\"MinReplayId\": 65536853", updated);
            Assert.Contains("\"CachedReplayLimit\": 9", updated);
            Assert.DoesNotContain("\"Old\"", updated);
            Assert.Contains("65536853", File.ReadAllText(install));
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
    public void PreserveMinReplayId_LeavesTheZipWhenTheInstallHasNoSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-min-replay-" + Path.GetRandomFileName());
        string staged = Path.Combine(root, "prepared", "appsettings.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.WriteAllText(staged, ZipSettings);

            ReleaseInstall.PreserveMinReplayId(Path.Combine(root, "missing.json"), staged);

            Assert.Equal(ZipSettings, File.ReadAllText(staged));
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
    public void PreserveCommand_WritesTheHigherMachineId()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-min-replay-" + Path.GetRandomFileName());
        string previous = Path.Combine(root, "previous.json");
        string target = Path.Combine(root, "target.json");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(previous, "{ \"MinReplayId\": 65536853 }");
            File.WriteAllText(target, ZipSettings);

            var rootCommand = new HeroesReplayCommand();
            ParseResult parsed = rootCommand.Parse(
                $"update preserve-min-replay-id --previous \"{previous}\" --target \"{target}\""
            );
            Assert.Empty(parsed.Errors);
            Assert.Equal(0, parsed.Invoke());

            string updated = File.ReadAllText(target);
            Assert.Contains("\"MinReplayId\": 65536853", updated);
            Assert.Contains("\"CachedReplayLimit\": 9", updated);
            Assert.Equal("{ \"MinReplayId\": 65536853 }", File.ReadAllText(previous));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
