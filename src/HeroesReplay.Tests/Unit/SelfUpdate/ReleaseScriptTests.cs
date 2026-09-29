using System;
using System.IO;
using Xunit;

namespace HeroesReplay.Tests.Unit.SelfUpdate;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReleaseScriptTests
{
    [Fact]
    public void ApplyRelease_OverwritesFilesAndClearsTheStopFile()
    {
        string script = File.ReadAllText(FindScript());

        Assert.DoesNotContain("Rename-Item", script);
        Assert.Contains("services.stop", script);
        Assert.Contains("HeroesReplay-live", script);
        Assert.Contains("start-live.cmd", script);
        Assert.Contains("heroesreplay is still running", script);
        Assert.Contains("robocopy.exe", script);
        Assert.Contains("preserve-min-replay-id", script);
    }

    private static string FindScript()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "tools", "apply-release.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("tools/apply-release.ps1");
    }
}
