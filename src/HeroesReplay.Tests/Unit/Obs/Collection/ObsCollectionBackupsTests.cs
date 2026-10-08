using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HeroesReplay.Core.Obs.Collection;
using Xunit;
using static HeroesReplay.Tests.Unit.Obs.Collection.ObsCollectionFixture;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

/// <summary>#307: <c>obs backup</c> and <c>obs restore</c>.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ObsCollectionBackupsTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 8, 14, 0, 0, DateTimeKind.Utc);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-backup-" + Path.GetRandomFileName()
    );

    public ObsCollectionBackupsTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Live));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Live => Path.Combine(root, "obs-studio", "basic", "scenes", "HeroesReplay.json");

    private ObsManagedFiles Managed => new(Path.Combine(root, "managed"));

    [Fact]
    public void BackupThenRestore_IsAByteForByteRoundTrip()
    {
        byte[] original = Encoding.UTF8.GetBytes(Layout(rankX: 1712.25) + "\r\n");
        File.WriteAllBytes(Live, original);

        ObsBackupResult taken = ObsCollectionBackups.Take(Managed, Live, Now);
        File.WriteAllText(Live, Layout(css: "body{changed}"));
        ObsBackupResult restored = ObsCollectionBackups.Restore(
            Managed,
            Live,
            Path.GetFileName(taken.Backup),
            obsIsRunning: false,
            Now.AddMinutes(5)
        );

        Assert.True(taken.Ok, taken.Message);
        Assert.Equal(ObsBackupCodes.BackedUp, taken.Code);
        Assert.Equal(original, File.ReadAllBytes(taken.Backup));
        Assert.True(restored.Ok, restored.Message);
        Assert.Equal(ObsBackupCodes.Restored, restored.Code);
        Assert.Equal(original, File.ReadAllBytes(Live));
        // The collection it replaced is a backup too, so the restore can be undone.
        Assert.Contains("body{changed}", File.ReadAllText(restored.Saved));
        Assert.Equal(2, restored.Backups.Count);
        Assert.Equal(restored.Saved, restored.Backups[0].Path);
    }

    [Fact]
    public void Restore_IsRefusedWhileOBSRuns()
    {
        File.WriteAllText(Live, Layout());
        string backup = ObsCollectionBackups.Take(Managed, Live, Now).Backup;
        File.WriteAllText(Live, Layout(css: "body{live}"));

        ObsBackupResult result = ObsCollectionBackups.Restore(
            Managed,
            Live,
            backup,
            obsIsRunning: true,
            Now
        );

        Assert.False(result.Ok);
        Assert.Equal(ObsBackupCodes.ObsRunning, result.Code);
        Assert.Contains("body{live}", File.ReadAllText(Live));
        Assert.Single(ObsFileTransaction.Backups(Managed.BackupDirectory, Live));
    }

    [Fact]
    public void Restore_RefusesAnotherFilesBackup_AndOneThatIsNotACollection()
    {
        File.WriteAllText(Live, Layout());
        string profile = Path.Combine(
            root,
            "obs-studio",
            "basic",
            "profiles",
            "HeroesReplay",
            "basic.ini"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(profile));
        File.WriteAllText(profile, "[General]");
        string profileBackup = ObsFileTransaction.Snapshot(profile, Managed.BackupDirectory, Now);
        File.WriteAllText(Live, "{ \"name\": \"HeroesReplay\" }");
        string notCollection = ObsCollectionBackups.Take(Managed, Live, Now.AddSeconds(1)).Backup;
        string current = Layout();
        File.WriteAllText(Live, current);

        ObsBackupResult other = ObsCollectionBackups.Restore(
            Managed,
            Live,
            profileBackup,
            false,
            Now
        );
        ObsBackupResult invalid = ObsCollectionBackups.Restore(
            Managed,
            Live,
            notCollection,
            false,
            Now
        );
        ObsBackupResult missing = ObsCollectionBackups.Restore(
            Managed,
            Live,
            "nope.bak",
            false,
            Now
        );

        Assert.Equal(ObsBackupCodes.BackupOther, other.Code);
        Assert.Equal(ObsBackupCodes.BackupInvalid, invalid.Code);
        Assert.Equal(ObsBackupCodes.BackupMissing, missing.Code);
        Assert.All(new[] { other, invalid, missing }, result => Assert.False(result.Ok));
        Assert.Equal(current, File.ReadAllText(Live));
    }

    [Fact]
    public void Restore_ClearsARollbackThatWaitedForOBS()
    {
        File.WriteAllText(Live, Layout());
        string backup = ObsCollectionBackups.Take(Managed, Live, Now).Backup;
        File.WriteAllText(Live, Layout(css: "body{failed}"));
        Managed.SavePendingRestore(
            new ObsPendingRestore
            {
                CollectionPath = Live,
                Backup = backup,
                TemplateSha256 = "ABC",
            }
        );

        ObsBackupResult result = ObsCollectionBackups.Restore(
            Managed,
            Live,
            backup,
            false,
            Now.AddMinutes(1)
        );
        ObsBackupResult again = ObsCollectionBackups.Restore(
            Managed,
            Live,
            backup,
            false,
            Now.AddMinutes(2)
        );

        Assert.Equal(ObsBackupCodes.Restored, result.Code);
        Assert.Null(Managed.ReadPendingRestore());
        Assert.Equal(ObsBackupCodes.AlreadyRestored, again.Code);
        Assert.True(again.Ok);
    }

    [Fact]
    public void Backup_WithoutACollection_IsNotOk_AndTheJsonIsStable()
    {
        ObsBackupResult result = ObsCollectionBackups.Take(Managed, Live, Now);

        Assert.False(result.Ok);
        Assert.Equal(ObsBackupCodes.CollectionMissing, result.Code);
        using JsonDocument document = JsonDocument.Parse(result.ToJson());
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(
            ObsBackupCodes.CollectionMissing,
            document.RootElement.GetProperty("code").GetString()
        );
        Assert.Empty(ObsCollectionBackups.List(Managed, Live).Backups);
    }

    [Fact]
    public void List_ShowsTheNewestFirst_WithTheirHashes()
    {
        File.WriteAllText(Live, "{\"sources\":[]}");
        ObsCollectionBackups.Take(Managed, Live, Now);
        File.WriteAllText(Live, "{\"sources\":[{\"name\":\"x\"}]}");
        ObsCollectionBackups.Take(Managed, Live, Now.AddMinutes(1));

        ObsBackupResult list = ObsCollectionBackups.List(Managed, Live);

        Assert.True(list.Ok);
        Assert.Equal(ObsBackupCodes.Listed, list.Code);
        Assert.Equal(
            new DateTime?[] { Now.AddMinutes(1), Now },
            list.Backups.Select(b => b.TakenAtUtc)
        );
        Assert.All(list.Backups, backup => Assert.Equal(64, backup.Sha256.Length));
    }
}
