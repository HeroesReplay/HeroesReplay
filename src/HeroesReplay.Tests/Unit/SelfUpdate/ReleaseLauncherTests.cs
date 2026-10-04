using System;
using System.IO;
using HeroesReplay.Core.SelfUpdate;
using Xunit;

namespace HeroesReplay.Tests.Unit.SelfUpdate;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReleaseLauncherTests
{
    // The stream PC's launcher until 2026-10-04: four cmd /k windows, no services start.
    private const string CmdWindows = """
        @echo off
        setlocal
        set HEROES_REPLAY_ENV=prod
        set HEROES_REPLAY_OBS__StreamingEnabled=true
        set BIN=C:\heroesreplay\app
        cd /d "%BIN%"
        start "hr-download" cmd /k ""%BIN%\heroesreplay.exe" heroesprofile download"
        start "hr-twitch" cmd /k ""%BIN%\heroesreplay.exe" twitch connect"
        start "hr-youtube" cmd /k ""%BIN%\heroesreplay.exe" youtube uploader"
        start "hr-spectate" cmd /k ""%BIN%\heroesreplay.exe" spectate heroesprofile"
        exit /b 0
        """;

    // Its stand-in while the Twitch scope check failed: supervised, but without twitch.
    private const string WithoutTwitch = """
        @echo off
        setlocal
        set HEROES_REPLAY_ENV=prod
        set HEROES_REPLAY_OBS__StreamingEnabled=true
        cd /d "C:\heroesreplay\app"
        "C:\heroesreplay\app\heroesreplay.exe" services start --supervise --roles spectate,download,youtube > %LOCALAPPDATA%\HeroesReplay\logs\services-start-console.log 2>&1
        """;

    [Theory]
    [InlineData(CmdWindows)]
    [InlineData(WithoutTwitch)]
    public void Rewrite_StartsEveryRoleSupervisedAndKeepsTheSettings(string current)
    {
        string launcher = ReleaseLauncher.Rewrite(current, @"C:\heroesreplay\app\", "prod");

        Assert.Contains("set HEROES_REPLAY_ENV=prod\r\n", launcher, StringComparison.Ordinal);
        Assert.Contains(
            "set HEROES_REPLAY_OBS__StreamingEnabled=true\r\n",
            launcher,
            StringComparison.Ordinal
        );
        Assert.EndsWith(
            "cd /d \"C:\\heroesreplay\\app\"\r\n\"C:\\heroesreplay\\app\\heroesreplay.exe\" services start --supervise\r\n",
            launcher,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("cmd /k", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("--roles", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("services-start-console.log", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("set BIN=", launcher, StringComparison.Ordinal);
        Assert.Equal(2, launcher.Split("HEROES_REPLAY_ENV=").Length);
    }

    [Fact]
    public void Rewrite_TakesTheEnvironmentItIsGiven()
    {
        string launcher = ReleaseLauncher.Rewrite(CmdWindows, @"C:\e2e\app", "dev");

        Assert.Contains("set HEROES_REPLAY_ENV=dev\r\n", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("HEROES_REPLAY_ENV=prod", launcher, StringComparison.Ordinal);
    }

    [Fact]
    public void Replace_KeepsTheOldLauncherAndRestoreBringsItBack()
    {
        string state = Path.Combine(Path.GetTempPath(), "hr-launcher-" + Path.GetRandomFileName());
        Directory.CreateDirectory(state);
        string launcher = Path.Combine(state, ReleaseLauncher.FileName);
        string backup = Path.Combine(state, ReleaseLauncher.BackupName);
        try
        {
            File.WriteAllText(launcher, WithoutTwitch);

            string replaced = ReleaseLauncher.Replace(state, @"C:\heroesreplay\app", "prod");

            Assert.StartsWith("Rewrote", replaced, StringComparison.Ordinal);
            Assert.Equal(WithoutTwitch, File.ReadAllText(backup));
            Assert.DoesNotContain("--roles", File.ReadAllText(launcher), StringComparison.Ordinal);

            // A rollback restores the install that the old launcher started.
            ReleaseLauncher.Restore(state);
            Assert.Equal(WithoutTwitch, File.ReadAllText(launcher));

            // The next release finds it rewritten already and changes nothing.
            ReleaseLauncher.Replace(state, @"C:\heroesreplay\app", "prod");
            string again = ReleaseLauncher.Replace(state, @"C:\heroesreplay\app", "prod");
            Assert.Contains("already", again, StringComparison.Ordinal);
            Assert.Equal(File.ReadAllText(launcher), File.ReadAllText(backup));
        }
        finally
        {
            Directory.Delete(state, recursive: true);
        }
    }

    [Fact]
    public void Replace_WithoutALauncher_DropsAStaleBackupSoARollbackRestoresNothing()
    {
        string state = Path.Combine(Path.GetTempPath(), "hr-launcher-" + Path.GetRandomFileName());
        Directory.CreateDirectory(state);
        try
        {
            File.WriteAllText(Path.Combine(state, ReleaseLauncher.BackupName), CmdWindows);

            ReleaseLauncher.Replace(state, @"C:\heroesreplay\app", "prod");
            string restored = ReleaseLauncher.Restore(state);

            Assert.False(File.Exists(Path.Combine(state, ReleaseLauncher.FileName)));
            Assert.StartsWith("No ", restored, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(state, recursive: true);
        }
    }
}
