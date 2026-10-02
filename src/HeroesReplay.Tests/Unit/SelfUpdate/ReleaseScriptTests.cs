using System;
using System.IO;
using System.Runtime.CompilerServices;
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
        Assert.Contains("Protect-MinReplayId (Join-Path $source 'heroesreplay.exe')", script);
        Assert.Contains("stabilization window", script);
        Assert.Contains("role-ready.txt", script);
        Assert.Contains("update release-health", script);
        Assert.DoesNotContain("CreationTime", script);
    }

    [Fact]
    public void ApplyRelease_StabilizationWindowStillInstallsInsteadOfLeavingTheStackStopped()
    {
        string script = File.ReadAllText(FindScript());
        int health = script.IndexOf("update release-health", StringComparison.Ordinal);
        int backup = script.IndexOf("if ($backUpInstall)", StringComparison.Ordinal);

        Assert.True(health > 0 && backup > health);
        string branch = script.Substring(health, backup - health);
        Assert.Contains("$backUpInstall = $false", branch);
        Assert.DoesNotContain("exit", branch);
    }

    private static string FindScript([CallerFilePath] string sourceFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? AppContext.BaseDirectory);
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
