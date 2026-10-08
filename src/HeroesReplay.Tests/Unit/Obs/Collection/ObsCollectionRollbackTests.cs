using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.SelfUpdate;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

/// <summary>
/// #304: a release rollback puts back the scene collection the restored build ran with, from
/// the backup taken before the failed release first wrote it.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ObsCollectionRollbackTests : IDisposable
{
    private static readonly DateTime Installed = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime RolledBack = Installed.AddMinutes(21);

    /// <summary>
    /// The collection the previous build ran with, as OBS saved it: its own spacing, line ends,
    /// and the operator's positions. A rollback must put back exactly these bytes.
    /// </summary>
    private static readonly byte[] Original = Encoding.UTF8.GetBytes(
        "{\r\n  \"current_scene\": \"waiting\",\r\n  \"name\": \"HeroesReplay\",\r\n  \"sources\": [ { \"name\": \"rank-image\", \"id\": \"image_source\", \"settings\": { \"x\": 412.5 } } ]\r\n}\r\n"
    );

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-rollback-" + Path.GetRandomFileName()
    );

    public ObsCollectionRollbackTests()
    {
        Directory.CreateDirectory(Scenes);
        WriteTemplate(Previous, "rank-image");
        WriteTemplate(Failed, "rank-image", "queue-browser");
        File.WriteAllBytes(Live, Original);
        // The previous release wrote the live collection from its template.
        Managed.Save(
            Live,
            new ObsManagedCollection(PreviousHash, ["rank-image"], Installed.AddDays(-3))
        );
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string AppData => Path.Combine(root, "appdata");

    private string Scenes => Path.Combine(AppData, "obs-studio", "basic", "scenes");

    private string Live => Path.Combine(Scenes, "HeroesReplay.json");

    private string Previous => Path.Combine(root, "app.previous");

    private string Failed => Path.Combine(root, "app");

    private ObsManagedFiles Managed => new(Path.Combine(root, "managed"));

    private string PreviousHash =>
        ObsCollectionPatcher.TemplateHash(Path.Combine(Previous, "obs", "Default.json"));

    [Fact]
    public void Restore_WithOBSClosed_PutsTheBackupsBytesBack_AndTheRestoredBuildKeepsThem()
    {
        IReadOnlyList<string> notes = InstallFailedRelease(obsIsRunning: false);
        Assert.Contains("queue-browser", File.ReadAllText(Live));
        Assert.Contains(
            notes,
            note =>
                note.StartsWith(
                    "OBS rollback: A rollback of this release puts back",
                    StringComparison.Ordinal
                )
        );

        ObsRollbackResult result = Rollback(obsIsRunning: false);

        Assert.Equal(ObsRollbackOutcome.Restored, result.Outcome);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(Original, File.ReadAllBytes(Live));
        Assert.Equal(Original, File.ReadAllBytes(result.Backup));
        Assert.Equal(PreviousHash, Managed.Read(Live).TemplateSha256);
        Assert.Equal(["rank-image"], Managed.Read(Live).Sources);
        Assert.Null(Managed.ReadRollback());
        // The failed release's collection is kept as a backup too.
        Assert.Contains(
            ObsFileTransaction.Backups(Managed.BackupDirectory, Live),
            backup => File.ReadAllText(backup).Contains("queue-browser", StringComparison.Ordinal)
        );

        // apply-release.ps1 then runs the restored build's install-obs: it keeps the file.
        IReadOnlyList<string> restored = ReleaseInstall.InstallObsFiles(
            new ReleaseObsInstall
            {
                InstallDirectory = Previous,
                AppData = AppData,
                Managed = Managed,
                UtcNow = RolledBack.AddMinutes(1),
            }
        );
        Assert.Equal(Original, File.ReadAllBytes(Live));
        Assert.Contains(restored, note => note.Contains("already match", StringComparison.Ordinal));
    }

    [Fact]
    public void Restore_WhenTheReleaseNeverWroteTheCollection_ChangesNothing()
    {
        // No record before the install: the release saves one, then defers while OBS runs.
        File.Delete(Managed.RecordPath);
        InstallFailedRelease(obsIsRunning: true);
        Assert.Equal(Original, File.ReadAllBytes(Live));
        Assert.Equal(ObsCollectionPatcher.UnknownTemplate, Managed.Read(Live).TemplateSha256);

        ObsRollbackResult result = Rollback(obsIsRunning: false);

        Assert.Equal(ObsRollbackOutcome.NotWritten, result.Outcome);
        Assert.True(result.Ok);
        Assert.Contains("nothing to put back", result.Message);
        Assert.Equal(Original, File.ReadAllBytes(Live));
        Assert.Empty(ObsFileTransaction.Backups(Managed.BackupDirectory, Live));
        // The record the release saved goes too: it is as it was before the install.
        Assert.Null(Managed.Read(Live));
        Assert.Null(Managed.ReadRollback());
    }

    [Fact]
    public void Restore_UsesTheFirstWriteAfterTheInstall_WhenTheReleaseWroteTheCollectionLater()
    {
        // An older backup, from before this release, is never the rollback source.
        ObsFileTransaction.Write(Live, Original, Managed.BackupDirectory, Installed.AddDays(-1));
        InstallFailedRelease(obsIsRunning: true);
        Assert.Equal(Original, File.ReadAllBytes(Live));

        // The failed build's services start finds OBS closed and replaces the collection.
        ObsCollectionApplyResult later = ObsCollectionPatcher.Apply(
            new ObsCollectionUpdate
            {
                TemplatePath = Path.Combine(Failed, "obs", "Default.json"),
                DestinationPath = Live,
                Managed = Managed,
                UtcNow = Installed.AddMinutes(5),
            }
        );
        Assert.True(later.Wrote, later.Message);
        // and once more, a path update, which must not become the source either.
        ObsFileTransaction.Write(
            Live,
            File.ReadAllBytes(Live),
            Managed.BackupDirectory,
            Installed.AddMinutes(9)
        );

        ObsRollbackResult result = Rollback(obsIsRunning: false);

        Assert.Equal(ObsRollbackOutcome.Restored, result.Outcome);
        Assert.Equal(later.Backup, result.Backup);
        Assert.Equal(Original, File.ReadAllBytes(Live));
    }

    [Fact]
    public void Restore_WhileOBSRuns_SwapsTheBackupInThroughTheSpareCollection()
    {
        InstallFailedRelease(obsIsRunning: false);
        var obs = new FakeCollectionSwitch(Scenes, "HeroesReplay") { Scene = "waiting" };

        ObsRollbackResult result = Rollback(obsIsRunning: true, () => obs);

        Assert.Equal(ObsRollbackOutcome.RestoredLive, result.Outcome);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(["HeroesReplay", "HeroesReplay-next", "HeroesReplay"], obs.Selected);
        Assert.Equal("HeroesReplay", obs.Current);
        Assert.Equal("waiting", obs.Scene);
        Assert.Equal(Encoding.UTF8.GetString(Original), obs.Loaded);
        Assert.Equal(Original, File.ReadAllBytes(Live));
        Assert.Equal(PreviousHash, Managed.Read(Live).TemplateSha256);
        Assert.True(obs.Disposed);
        Assert.Null(Managed.ReadPendingRestore());
    }

    [Fact]
    public void Restore_WhileOBSRunsAndTheSwapCannotRun_IsDeferredReported_AndLandsWhenOBSCloses()
    {
        InstallFailedRelease(obsIsRunning: false);
        byte[] failed = File.ReadAllBytes(Live);

        ObsRollbackResult result = Rollback(
            obsIsRunning: true,
            () =>
                throw new ObsUnavailableException(
                    ObsUnavailableException.Unreachable,
                    "OBS did not answer."
                )
        );

        Assert.Equal(ObsRollbackOutcome.Deferred, result.Outcome);
        Assert.True(result.Ok);
        Assert.Contains("OBS did not answer.", result.Message);
        Assert.Contains("services status", result.Message);
        Assert.Equal(failed, File.ReadAllBytes(Live));
        string waiting = ObsCollectionRollback.DescribePending(Managed);
        Assert.Contains(result.Backup, waiting);
        // Until it lands, the record still names the failed release's template, so a build
        // from before this rollback replaces the collection with its own.
        Assert.NotEqual(PreviousHash, Managed.Read(Live).TemplateSha256);

        // The restored build, at its next replay with OBS still running: a live swap of the backup.
        ObsCollectionApplyResult running = RestoredBuildApply(obsIsRunning: true);
        Assert.True(running.Deferred, running.Message);
        Assert.Equal(Encoding.UTF8.GetString(Original), running.Replacement.Contents);
        Assert.Equal(PreviousHash, running.Replacement.TemplateSha256);

        // Or once OBS is closed: the exact bytes, the record, and the wait is over.
        ObsCollectionApplyResult closed = RestoredBuildApply(obsIsRunning: false);
        Assert.True(closed.Wrote, closed.Message);
        Assert.StartsWith("Release rollback:", closed.Message, StringComparison.Ordinal);
        Assert.Equal(Original, File.ReadAllBytes(Live));
        Assert.Equal(PreviousHash, Managed.Read(Live).TemplateSha256);
        Assert.Null(Managed.ReadPendingRestore());
        Assert.Null(ObsCollectionRollback.DescribePending(Managed));
    }

    [Fact]
    public void PendingRestore_EndsWithTheSpectatorsLiveSwap()
    {
        InstallFailedRelease(obsIsRunning: false);
        Rollback(obsIsRunning: true, openSwitch: null);
        ObsCollectionApplyResult running = RestoredBuildApply(obsIsRunning: true);

        ObsLiveSwapResult swap = ObsLiveCollectionSwap.Run(
            new FakeCollectionSwitch(Scenes, "HeroesReplay"),
            running.Replacement,
            "HeroesReplay",
            Managed,
            RolledBack.AddMinutes(3),
            _ => { }
        );

        Assert.True(swap.Swapped, swap.Message);
        Assert.Equal(Original, File.ReadAllBytes(Live));
        Assert.Null(Managed.ReadPendingRestore());
        Assert.Equal(PreviousHash, Managed.Read(Live).TemplateSha256);
    }

    [Fact]
    public void PendingRestore_IsDroppedByAnotherInstall()
    {
        InstallFailedRelease(obsIsRunning: false);
        Rollback(obsIsRunning: true, openSwitch: null);
        Assert.NotNull(Managed.ReadPendingRestore());

        // A later release with a third template: the rollback no longer applies.
        string next = Path.Combine(root, "app.next");
        WriteTemplate(next, "rank-image", "queue-browser", "clip-player");
        ObsCollectionApplyResult result = ObsCollectionPatcher.Apply(
            new ObsCollectionUpdate
            {
                TemplatePath = Path.Combine(next, "obs", "Default.json"),
                DestinationPath = Live,
                Managed = Managed,
                Release = true,
                UtcNow = RolledBack.AddDays(1),
            }
        );

        Assert.True(result.Wrote, result.Message);
        Assert.Contains("clip-player", File.ReadAllText(Live));
        Assert.Null(Managed.ReadPendingRestore());
    }

    [Fact]
    public void Restore_NeverOverwritesACollectionChangedAfterTheReleaseWroteIt()
    {
        InstallFailedRelease(obsIsRunning: false);
        string custom = File.ReadAllText(Live)
            .Replace(
                "\"sources\":[",
                "\"sources\":[{\"name\":\"my-webcam\"},",
                StringComparison.Ordinal
            );
        File.WriteAllText(Live, custom);

        ObsRollbackResult result = Rollback(obsIsRunning: false);

        Assert.Equal(ObsRollbackOutcome.Custom, result.Outcome);
        Assert.False(result.Ok);
        Assert.Contains("my-webcam", result.Message);
        Assert.Contains(result.Backup, result.Message);
        Assert.Equal(custom, File.ReadAllText(Live));
        // Kept, so the rollback can run again once the operator removes the source.
        Assert.NotNull(Managed.ReadRollback());
    }

    [Fact]
    public void Restore_RefusesARecordWrittenForAnotherInstall()
    {
        InstallFailedRelease(obsIsRunning: false);
        byte[] failed = File.ReadAllBytes(Live);
        WriteTemplate(Previous, "rank-image", "something-else");

        ObsRollbackResult result = Rollback(obsIsRunning: false);

        Assert.Equal(ObsRollbackOutcome.OtherInstall, result.Outcome);
        Assert.False(result.Ok);
        Assert.Equal(failed, File.ReadAllBytes(Live));
    }

    [Fact]
    public void Restore_WithoutARecord_ChangesNothing()
    {
        ObsRollbackResult result = Rollback(obsIsRunning: false);

        Assert.Equal(ObsRollbackOutcome.NoRecord, result.Outcome);
        Assert.True(result.Ok);
        Assert.Equal(Original, File.ReadAllBytes(Live));
        Assert.Empty(ObsFileTransaction.Backups(Managed.BackupDirectory, Live));
    }

    [Fact]
    public void Restore_ASecondTime_FindsNothingToDo()
    {
        InstallFailedRelease(obsIsRunning: false);
        Assert.Equal(ObsRollbackOutcome.Restored, Rollback(obsIsRunning: false).Outcome);

        ObsRollbackResult again = Rollback(obsIsRunning: false);

        Assert.Equal(ObsRollbackOutcome.NoRecord, again.Outcome);
        Assert.Equal(Original, File.ReadAllBytes(Live));
    }

    /// <summary>The failed release's <c>update install-obs --previous</c>.</summary>
    private IReadOnlyList<string> InstallFailedRelease(bool obsIsRunning) =>
        ReleaseInstall.InstallObsFiles(
            new ReleaseObsInstall
            {
                InstallDirectory = Failed,
                AppData = AppData,
                ObsIsRunning = obsIsRunning,
                Managed = Managed,
                PreviousInstall = Previous,
                UtcNow = Installed,
            }
        );

    /// <summary>The failed build's <c>update restore-obs</c>.</summary>
    private ObsRollbackResult Rollback(
        bool obsIsRunning,
        Func<IObsCollectionSwitch> openSwitch = null
    ) =>
        ObsCollectionRollback.Restore(
            new ObsRollbackRequest
            {
                Managed = Managed,
                PreviousTemplatePath = Path.Combine(Previous, "obs", "Default.json"),
                FailedTemplatePath = Path.Combine(Failed, "obs", "Default.json"),
                ObsIsRunning = obsIsRunning,
                OpenSwitch = openSwitch,
                UtcNow = RolledBack,
                Wait = _ => { },
            }
        );

    /// <summary>The restored build's <c>services start</c> or next replay.</summary>
    private ObsCollectionApplyResult RestoredBuildApply(bool obsIsRunning) =>
        ObsCollectionPatcher.Apply(
            new ObsCollectionUpdate
            {
                TemplatePath = Path.Combine(Previous, "obs", "Default.json"),
                DestinationPath = Live,
                ObsIsRunning = obsIsRunning,
                Managed = Managed,
                UtcNow = RolledBack.AddMinutes(2),
            }
        );

    private static void WriteTemplate(string install, params string[] sources)
    {
        Directory.CreateDirectory(Path.Combine(install, "obs"));
        File.WriteAllText(
            Path.Combine(install, "obs", "Default.json"),
            "{\"name\":\"HeroesReplay\",\"sources\":["
                + string.Join(
                    ",",
                    Array.ConvertAll(
                        sources,
                        name =>
                            "{\"name\":\"" + name + "\",\"id\":\"image_source\",\"settings\":{}}"
                    )
                )
                + "]}"
        );
    }
}
