using System;
using System.IO;
using System.Runtime.CompilerServices;
using HeroesReplay.Core.SelfUpdate;
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
        Assert.DoesNotContain("role-ready.txt", script);
        Assert.DoesNotContain("--role-file", script);
        Assert.DoesNotContain("CreationTime", script);
    }

    [Fact]
    public void ApplyRelease_GatesTheStartedStackThenRefreshesOrRollsBack()
    {
        string script = File.ReadAllText(FindScript());
        int since = script.IndexOf(
            "$since = (Get-Date).ToUniversalTime().ToString('o')",
            StringComparison.Ordinal
        );
        int start = script.IndexOf("Start-HeroesReplayStack", since, StringComparison.Ordinal);
        int gate = script.IndexOf(
            "$health = Invoke-ReleaseHealth (Join-Path $InstallDir 'heroesreplay.exe') $since",
            StringComparison.Ordinal
        );
        int healthy = script.IndexOf("if ($health -eq 0)", gate, StringComparison.Ordinal);
        int inconclusive = script.IndexOf("if ($health -eq 4)", gate, StringComparison.Ordinal);
        int stopped = script.IndexOf("if ($health -eq 3)", gate, StringComparison.Ordinal);
        int skip = script.IndexOf("Add-SkippedRelease $Version", gate, StringComparison.Ordinal);
        int stop = script.IndexOf("Stop-HeroesReplayStack", skip, StringComparison.Ordinal);
        int restore = script.IndexOf(
            "Restore-PreviousInstall $previous",
            stop,
            StringComparison.Ordinal
        );
        int restart = script.IndexOf("Start-HeroesReplayStack", restore, StringComparison.Ordinal);

        Assert.True(
            since > 0 && start > since && gate > start,
            "The gate does not follow the start."
        );
        Assert.True(
            healthy > gate && inconclusive > healthy && stopped > inconclusive && skip > stopped
        );
        Assert.True(stop > skip && restore > stop && restart > restore);
        Assert.Contains(
            "Copy-Install $InstallDir $previous",
            script.Substring(healthy, inconclusive - healthy)
        );
        Assert.DoesNotContain("Restore-PreviousInstall", script.Substring(healthy, skip - healthy));

        // Inconclusive keeps the install: no refresh of .previous, no skip, no rollback.
        string keep = script.Substring(inconclusive, stopped - inconclusive);
        Assert.Contains("exit 0", keep);
        Assert.DoesNotContain("Copy-Install", keep);
        Assert.DoesNotContain("Add-SkippedRelease", keep);
        Assert.DoesNotContain("Stop-HeroesReplayStack", keep);
        Assert.Contains(
            "update release-health --since $Since --install `\"$InstallDir`\" --environment $environment --wait",
            script
        );
    }

    [Fact]
    public void ApplyRelease_AlwaysBacksUpTheRunningInstallBeforeItIsOverwritten()
    {
        string script = File.ReadAllText(FindScript());
        int migrate = script.IndexOf("'update', 'migrate-stream-arm'", StringComparison.Ordinal);
        int backup = script.IndexOf(
            "Copy-Install $InstallDir $previous",
            migrate,
            StringComparison.Ordinal
        );
        int copy = script.IndexOf(
            "Copy-Item -Path (Join-Path $source '*') -Destination $InstallDir",
            StringComparison.Ordinal
        );

        Assert.True(migrate > 0 && backup > migrate && copy > backup);
        string block = script.Substring(backup, copy - backup);
        Assert.Contains("was not installed", block);
        Assert.Contains("Start-HeroesReplayStack", block);
        Assert.Contains("exit 1", block);
        // An older .previous is never kept in place of the install being replaced.
        int guard = script.LastIndexOf(
            "if (Test-Path -LiteralPath $previous)",
            backup,
            StringComparison.Ordinal
        );
        Assert.True(guard < migrate, "The backup must not depend on an existing .previous.");
    }

    [Fact]
    public void ApplyRelease_SkipListMatchesTheGateAndRestartsSupervisedWhenItWas()
    {
        string script = File.ReadAllText(FindScript());

        Assert.Contains(@"'updates\" + ReleaseSkipList.FileName + "'", script);
        Assert.Contains("\"$Tag`t$when`t$note\"", script);
        Assert.Contains("if (Test-ReleaseSkipped $Version)", script);
        Assert.Contains("[switch]$Supervise", script);
        Assert.Contains("$arguments += '--supervise'", script);
        Assert.Contains("'supervisor.json'", script);
        Assert.Contains("@('services', 'stop')", script);
    }

    [Fact]
    public void ApplyRelease_StartsTheStackWithoutTheTaskAndInItsOwnEnvironment()
    {
        // 2026-10-02 on ASA-SERVER: with no HeroesReplay-live task, Windows PowerShell turned
        // schtasks' stderr into a terminating error under Stop, and the stack never started.
        string script = File.ReadAllText(FindScript());

        Assert.DoesNotContain("/Query /TN HeroesReplay-live *> $null", script);
        Assert.Contains("/Query /TN HeroesReplay-live >nul 2>&1", script);
        Assert.DoesNotContain("$env:HEROES_REPLAY_ENV = 'prod'", script);
        Assert.Contains("$env:HEROES_REPLAY_ENV = $environment", script);
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
