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

    [Fact]
    public void ApplyRelease_LeavesTheProfileToTheMachineAndNeverCopiesTheStreamKey()
    {
        string script = File.ReadAllText(FindScript());

        // The build applies the OBS files: the collection while OBS is closed, the profile only
        // when the machine has none (ReleaseInstall.CopyObsScenesIfClosed).
        Assert.Contains("'update', 'install-obs', '--install', $InstallDir", script);
        Assert.DoesNotContain("Default\\basic.ini'", script);
        Assert.DoesNotContain("profiles\\HeroesReplay", script);
        Assert.DoesNotContain("scenes\\HeroesReplay.json", script);
        Assert.DoesNotContain("Copy-Item -LiteralPath $ini", script);
        Assert.DoesNotContain("Copy-Item -LiteralPath $scene", script);
        Assert.Contains(
            "-Filter service.json -ErrorAction SilentlyContinue | Remove-Item -Force",
            script
        );
    }

    [Fact]
    public void ApplyRelease_MigratesTheStreamArmFromTheReplacedInstallBeforeItIsOverwritten()
    {
        string script = File.ReadAllText(FindScript());
        int migrate = script.IndexOf("'update', 'migrate-stream-arm'", StringComparison.Ordinal);
        int copy = script.IndexOf(
            "Copy-Item -Path (Join-Path $source '*') -Destination $InstallDir",
            StringComparison.Ordinal
        );
        int start = script.LastIndexOf("Start-HeroesReplayStack", StringComparison.Ordinal);

        Assert.True(migrate > 0, "apply-release.ps1 does not run the stream arm migration.");
        Assert.True(copy > migrate && start > migrate);
        Assert.Contains("'--previous', $InstallDir", script);
        Assert.Contains("'--environment', $environment", script);
        Assert.Contains(
            "(Join-Path $source 'heroesreplay.exe') @('update', 'migrate-stream-arm'",
            script
        );
        Assert.DoesNotContain("stream-armed'", script);
        Assert.Contains("Start-Transcript", script);
    }

    [Fact]
    public void Bootstrap_KeepsAnExistingProfile()
    {
        string script = File.ReadAllText(FindScript("bootstrap-workstation.ps1"));

        Assert.Contains("Test-Path -LiteralPath $profileIni", script);
        Assert.Contains("OBS profile kept", script);
        Assert.DoesNotContain(
            "Copy-Item -Force (Join-Path $root 'obs\\Default\\basic.ini')",
            script
        );
        Assert.Contains("[string]$ProfileName = 'HeroesReplay'", script);
        Assert.Contains("[string]$SceneCollectionName = 'HeroesReplay'", script);
        Assert.DoesNotContain("service.json'", script);
    }

    private static string FindScript(
        string name = "apply-release.ps1",
        [CallerFilePath] string sourceFile = ""
    )
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "tools", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("tools/" + name);
    }
}
