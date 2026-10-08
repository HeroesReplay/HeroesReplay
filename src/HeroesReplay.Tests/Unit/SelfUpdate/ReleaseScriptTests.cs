using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Core.ServiceHost;
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
    public void ApplyRelease_RewritesTheLauncherBeforeTheStartAndRestoresItOnARollback()
    {
        string script = File.ReadAllText(FindScript());
        int obs = script.IndexOf("'update', 'install-obs', '--install'", StringComparison.Ordinal);
        int rewrite = script.IndexOf(
            "@('update', 'launcher', '--install', $InstallDir, '--environment', $environment)",
            StringComparison.Ordinal
        );
        int since = script.IndexOf(
            "$since = (Get-Date).ToUniversalTime().ToString('o')",
            StringComparison.Ordinal
        );
        int skip = script.IndexOf("Add-SkippedRelease $Version", since, StringComparison.Ordinal);
        int stop = script.IndexOf("Stop-HeroesReplayStack", skip, StringComparison.Ordinal);
        int restore = script.IndexOf(
            "@('update', 'launcher', '--restore')",
            StringComparison.Ordinal
        );
        int restoreInstall = script.IndexOf(
            "Restore-PreviousInstall $previous",
            stop,
            StringComparison.Ordinal
        );

        Assert.True(
            obs > 0 && rewrite > obs && since > rewrite,
            "The new build must rewrite the launcher after its files are in and before it starts."
        );
        Assert.True(
            stop > skip && restore > stop && restoreInstall > restore,
            "A rollback must put the launcher back with the new exe, before the old install returns."
        );
    }

    [Fact]
    public void ApplyRelease_InstallsTheClipToolsBeforeTheStartAndOnlyWarns()
    {
        string script = File.ReadAllText(FindScript());
        int rewrite = script.IndexOf(
            "@('update', 'launcher', '--install', $InstallDir, '--environment', $environment)",
            StringComparison.Ordinal
        );
        int tools = script.IndexOf(
            "Install-ClipTools (Join-Path $InstallDir 'heroesreplay.exe')",
            StringComparison.Ordinal
        );
        int since = script.IndexOf(
            "$since = (Get-Date).ToUniversalTime().ToString('o')",
            StringComparison.Ordinal
        );
        int start = script.IndexOf("function Install-ClipTools", StringComparison.Ordinal);
        int end = script.IndexOf("function Invoke-ReleaseHealth", StringComparison.Ordinal);
        string install = script.Substring(start, end - start);

        Assert.True(
            rewrite > 0 && tools > rewrite && since > tools,
            "The new build installs ffmpeg after its files are in and before the stack starts."
        );
        Assert.Contains("@('deps', 'install')", install);
        Assert.Contains("WARNING", install);
        // A failed install never blocks, skips, or rolls back the release.
        Assert.DoesNotMatch(@"(?m)^\s*exit\b", install);
        Assert.DoesNotContain("throw", install);
        Assert.DoesNotContain("Restore-PreviousInstall", install);
        Assert.DoesNotContain("Add-SkippedRelease", install);
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
    public void ApplyRelease_AlwaysLeavesTheStackSupervised()
    {
        string script = File.ReadAllText(FindScript());
        int start = script.IndexOf("function Start-HeroesReplayStack", StringComparison.Ordinal);
        int end = script.IndexOf("function Test-SupervisorRunning", StringComparison.Ordinal);
        string startStack = script.Substring(start, end - start);

        // The direct start is supervised whether or not the replaced stack was.
        Assert.DoesNotContain("if ($Supervise)", startStack);
        Assert.Contains("$arguments += '--supervise'", startStack);
        // A task or start-live.cmd that starts it unsupervised gets a supervisor attached.
        Assert.Equal(2, CountOf(startStack, "Confirm-Supervised"));
        Assert.Contains($"'{ServiceSupervisorFile.MutexName}'", script);
        Assert.Contains("@('services', 'supervise')", script);
        Assert.Contains("services status --output json 2>nul", script);
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (
            int index = text.IndexOf(value, StringComparison.Ordinal);
            index >= 0;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)
        )
        {
            count++;
        }

        return count;
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

        // The build applies the OBS files (ReleaseInstall.InstallObsFiles): a managed collection is
        // replaced after a backup, the profile only when the machine has none. The install compares
        // the live collection with the replaced install's template too; the rollback has none.
        Assert.Contains(
            "'update', 'install-obs', '--install', $InstallDir, '--previous', $previous, '--environment'",
            script
        );
        Assert.Contains(
            "'update', 'install-obs', '--install', $InstallDir, '--environment'",
            script
        );
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
    public void ApplyRelease_StartsTheLogBeforeAnyExitAndStopsItInAFinally()
    {
        string script = File.ReadAllText(FindScript());
        Match start = Regex.Match(script, @"(?m)^Start-UpdateLog\r?\ntry \{");
        MatchCollection exits = Regex.Matches(script, @"(?m)^\s*exit\s+\d");
        int refuse = script.IndexOf("Refusing to replace a source build", StringComparison.Ordinal);
        int stop = script.LastIndexOf("finally {", StringComparison.Ordinal);

        Assert.True(start.Success, "The update log does not start right before the main try.");
        Assert.True(refuse > start.Index && refuse < stop);
        Assert.NotEmpty(exits);
        Assert.All(
            exits,
            exit =>
                Assert.True(
                    exit.Index > start.Index && exit.Index < stop,
                    "An exit outside the try would leave the log open: " + exit.Value
                )
        );
        Assert.Contains("Stop-UpdateLog", script.Substring(stop));
        Assert.Contains("Stop-Transcript", script);
        Assert.Contains("'apply-release-' + ", script);
        Assert.Contains("Update log: $fallback", script);
    }

    [Fact]
    public void ApplyRelease_WritesAFallbackLogWhenAnotherWindowHoldsTheLog()
    {
        // #281: a hand run with -NoExit kept its transcript on apply-release.log, and Windows
        // PowerShell 5.1 started each later update's transcript without an error but wrote nothing.
        using var sandbox = new ScriptSandbox();
        string fallback = Path.Combine(sandbox.Logs, "apply-release-v0.0.0-test.log");
        using var holder = new FileStream(
            sandbox.MainLog,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read
        );

        (int code, string output) = sandbox.Run(fallback);

        Assert.Equal(0, code);
        Assert.Equal(
            "Update log: " + fallback,
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim()
        );
        Assert.Equal(0, holder.Length);
        string log = File.ReadAllText(fallback);
        Assert.Contains("apply-release.log could not be written", log);
        Assert.Contains("Refusing to replace a source build", log);
        // The session goes on after the script, as a -NoExit window does, and the log is free.
        Assert.Contains("log released", output);
    }

    [Fact]
    public void ApplyRelease_WritesTheMainLogAndReleasesItWhenTheScriptEnds()
    {
        using var sandbox = new ScriptSandbox();

        (int code, string output) = sandbox.Run(sandbox.MainLog);

        Assert.Equal(0, code);
        Assert.Contains("Refusing to replace a source build", File.ReadAllText(sandbox.MainLog));
        Assert.DoesNotContain("Update log:", output);
        Assert.Single(Directory.GetFiles(sandbox.Logs, "apply-release*.log"));
        Assert.Contains("log released", output);
    }

    /// <summary>
    /// Runs apply-release.ps1 under Windows PowerShell 5.1, the host the spectator starts it with,
    /// with LOCALAPPDATA in a temp folder and an InstallDir under \worktrees\, so the script
    /// refuses right after its log starts and touches nothing else. The harness then opens the log
    /// exclusively in the same session.
    /// </summary>
    private sealed class ScriptSandbox : IDisposable
    {
        private const string Harness = """
            param([string]$Script, [string]$Root, [string]$Log)
            try {
                & $Script -InstallDir (Join-Path $Root 'worktrees\app') -StagingDir (Join-Path $Root 'staging') -Version 'v0.0.0-test'
            }
            catch {
                Write-Host "refused: $($_.Exception.Message)"
            }

            try {
                [System.IO.File]::Open($Log, 'Open', 'ReadWrite', 'None').Dispose()
                Write-Host 'log released'
            }
            catch {
                Write-Host "log still held: $($_.Exception.Message)"
            }
            """;

        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            "hr-apply-release-" + Path.GetRandomFileName()
        );

        public ScriptSandbox()
        {
            Logs = Path.Combine(root, "LocalAppData", "HeroesReplay", "logs");
            Directory.CreateDirectory(Logs);
            File.WriteAllText(Path.Combine(root, "harness.ps1"), Harness);
        }

        public string Logs { get; }

        public string MainLog => Path.Combine(Logs, "apply-release.log");

        public (int Code, string Output) Run(string log)
        {
            var start = new ProcessStartInfo(
                Path.Combine(
                    Environment.SystemDirectory,
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe"
                )
            )
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (
                string argument in new[]
                {
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    Path.Combine(root, "harness.ps1"),
                    "-Script",
                    FindScript(),
                    "-Root",
                    root,
                    "-Log",
                    log,
                }
            )
            {
                start.ArgumentList.Add(argument);
            }

            start.Environment["LOCALAPPDATA"] = Path.Combine(root, "LocalAppData");
            using Process process = Process.Start(start);
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(60_000), "powershell.exe did not exit.");
            process.WaitForExit();
            return (process.ExitCode, output.Result + error.Result);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Bootstrap_KeepsAnExistingCollectionAndProfile()
    {
        string script = File.ReadAllText(FindScript("bootstrap-workstation.ps1"));

        Assert.Contains("Test-Path -LiteralPath $collectionFile", script);
        Assert.Contains("OBS collection kept", script);
        Assert.DoesNotContain("WriteAllText($collectionFile", script);
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
