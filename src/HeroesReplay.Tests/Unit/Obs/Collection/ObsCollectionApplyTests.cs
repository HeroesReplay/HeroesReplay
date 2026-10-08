using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.Obs.Collection;
using Xunit;
using static HeroesReplay.Tests.Unit.Obs.Collection.ObsCollectionFixture;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

/// <summary>#307: <c>obs apply</c> merges template changes and keeps the operator's work.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ObsCollectionApplyTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-apply-" + Path.GetRandomFileName()
    );

    public ObsCollectionApplyTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Live));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Live =>
        Path.Combine(root, "appdata", "obs-studio", "basic", "scenes", "HeroesReplay.json");

    private string Install => Path.Combine(root, "app");

    private string Previous => Path.Combine(root, "app.previous");

    private ObsManagedFiles Managed => new(Path.Combine(root, "managed"));

    private static readonly string Webcam = Source("my-webcam", "dshow_input");

    [Fact]
    public void Apply_KeepsTheOperatorsSourceAndOverride_MigratesTheManagedChange_AndRestoreUndoesIt()
    {
        WriteTemplate(Previous, Layout(css: "body{}"));
        WriteTemplate(Install, Layout(css: "body{margin:0}"));
        string before = Layout(css: "body{}", rankX: 1700, extraSources: [Webcam]);
        File.WriteAllText(Live, before);
        ObsManagedCollection recorded = RecordLiveFrom(Previous);

        ObsApplyResult applied = Apply(write: true, previous: true);

        Assert.True(applied.Ok, applied.Message);
        Assert.Equal(ObsApplyCodes.Applied, applied.Code);
        Assert.Equal("previous", applied.Base);
        Assert.True(applied.Written);
        Assert.Equal(1, applied.Taken);
        Assert.Equal(2, applied.Kept);
        string merged = File.ReadAllText(Live);
        Assert.Contains("body{margin:0}", merged, StringComparison.Ordinal);
        Assert.Contains("my-webcam", merged, StringComparison.Ordinal);
        Assert.Contains("1700", merged, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(applied.Backup));
        ObsManagedCollection record = Managed.Read(Live);
        Assert.Equal(TemplateHash(Install), record.TemplateSha256);
        Assert.True(record.Merged);
        Assert.Contains("my-webcam", record.Sources);
        Assert.NotNull(Managed.ReadTemplate(TemplateHash(Install)));

        // obs restore of the backup apply took is a byte-for-byte round trip, record included.
        ObsBackupResult restored = ObsCollectionBackups.Restore(
            Managed,
            Live,
            Path.GetFileName(applied.Backup),
            obsIsRunning: false,
            Now.AddMinutes(1)
        );

        Assert.True(restored.Ok, restored.Message);
        Assert.Equal(before, File.ReadAllText(Live));
        Assert.Equal(recorded.TemplateSha256, Managed.Read(Live).TemplateSha256);
        Assert.False(Managed.Read(Live).Merged);
        Assert.Null(Managed.ReadApplyUndo());
        Assert.Contains("before it", restored.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_IsRefusedWhileObsRuns_AndWritesNothing()
    {
        WriteTemplate(Previous, Layout(css: "body{}"));
        WriteTemplate(Install, Layout(css: "body{margin:0}"));
        File.WriteAllText(Live, Layout(css: "body{}", extraSources: [Webcam]));
        RecordLiveFrom(Previous);
        Dictionary<string, byte[]> before = Snapshot();

        ObsApplyResult result = Apply(write: true, previous: true, obsIsRunning: true);

        Assert.False(result.Ok);
        Assert.Equal(ObsApplyCodes.ObsRunning, result.Code);
        AssertUnchanged(before);
    }

    [Fact]
    public void Apply_WithoutBackup_WritesNothing_AndSaysTheMergeIsReady()
    {
        WriteTemplate(Previous, Layout(css: "body{}"));
        WriteTemplate(Install, Layout(css: "body{margin:0}"));
        File.WriteAllText(Live, Layout(css: "body{}", extraSources: [Webcam]));
        RecordLiveFrom(Previous);
        Dictionary<string, byte[]> before = Snapshot();

        ObsApplyResult result = Apply(write: false, previous: true, obsIsRunning: true);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(ObsApplyCodes.Ready, result.Code);
        Assert.False(result.Written);
        Assert.Contains("--backup", result.Message, StringComparison.Ordinal);
        AssertUnchanged(before);
    }

    [Fact]
    public void Apply_RefusesAConflict_AndWritesNothing()
    {
        WriteTemplate(Previous, Layout(cropTop: "0"));
        WriteTemplate(Install, Layout(cropTop: "20"));
        File.WriteAllText(Live, Layout(cropTop: "10"));
        RecordLiveFrom(Previous);
        Dictionary<string, byte[]> before = Snapshot();

        ObsApplyResult result = Apply(write: true, previous: true);

        Assert.False(result.Ok);
        Assert.Equal(ObsApplyCodes.Conflict, result.Code);
        Assert.Equal("settings.top", Assert.Single(result.Blocking).Property);
        AssertUnchanged(before);
    }

    [Fact]
    public void Apply_FindsTheBaseInTheTemplateStore_AfterThePreviousInstallIsGone()
    {
        WriteTemplate(Previous, Layout(css: "body{}"));
        WriteTemplate(Install, Layout(css: "body{margin:0}"));
        File.WriteAllText(Live, Layout(css: "body{}", extraSources: [Webcam]));
        RecordLiveFrom(Previous);
        Managed.SaveTemplate(File.ReadAllText(TemplateOf(Previous)), Now);
        Directory.Delete(Previous, recursive: true);

        ObsApplyResult result = Apply(write: true, previous: false);

        Assert.True(result.Ok, result.Message);
        Assert.Equal("stored", result.Base);
        Assert.Contains("body{margin:0}", File.ReadAllText(Live), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_WithoutAKnownBase_Refuses()
    {
        WriteTemplate(Install, Layout(css: "body{margin:0}"));
        File.WriteAllText(Live, Layout(css: "body{}"));
        Managed.Save(
            Live,
            new ObsManagedCollection(ObsCollectionPatcher.UnknownTemplate, ["x"], Now)
        );
        Dictionary<string, byte[]> before = Snapshot();

        ObsApplyResult result = Apply(write: true, previous: false);

        Assert.False(result.Ok);
        Assert.Equal(ObsApplyCodes.BaseUnknown, result.Code);
        AssertUnchanged(before);
    }

    [Fact]
    public void Apply_InSync_WritesNothing()
    {
        WriteTemplate(Install, Layout());
        File.WriteAllText(Live, Layout());
        RecordLiveFrom(Install);
        Dictionary<string, byte[]> before = Snapshot();

        ObsApplyResult result = Apply(write: true, previous: false);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(ObsApplyCodes.InSync, result.Code);
        AssertUnchanged(before);
    }

    [Fact]
    public void Apply_IsRefused_WhileAReleaseRollbackWaits()
    {
        WriteTemplate(Previous, Layout(css: "body{}"));
        WriteTemplate(Install, Layout(css: "body{margin:0}"));
        File.WriteAllText(Live, Layout(css: "body{}"));
        RecordLiveFrom(Previous);
        string backup = Path.Combine(root, "managed", "backups", "scenes-HeroesReplay.json.bak");
        Directory.CreateDirectory(Path.GetDirectoryName(backup));
        File.WriteAllText(backup, Layout());
        Managed.SavePendingRestore(
            new ObsPendingRestore
            {
                CollectionPath = Live,
                Backup = backup,
                TemplateSha256 = "X",
                Reason = "OBS did not answer.",
            }
        );

        ObsApplyResult result = Apply(write: true, previous: true);

        Assert.False(result.Ok);
        Assert.Equal(ObsApplyCodes.RollbackPending, result.Code);
        Assert.Contains("body{}", File.ReadAllText(Live), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_Json_IsAStableEnvelope()
    {
        WriteTemplate(Previous, Layout(css: "body{}"));
        WriteTemplate(Install, Layout(css: "body{margin:0}"));
        File.WriteAllText(Live, Layout(css: "body{}"));
        RecordLiveFrom(Previous);

        using JsonDocument document = JsonDocument.Parse(
            Apply(write: false, previous: true).ToJson()
        );
        JsonElement json = document.RootElement;

        Assert.Equal(1, json.GetProperty("schemaVersion").GetInt32());
        Assert.True(json.GetProperty("ok").GetBoolean());
        Assert.Equal(ObsApplyCodes.Ready, json.GetProperty("code").GetString());
        Assert.Equal("previous", json.GetProperty("base").GetString());
        Assert.False(json.GetProperty("written").GetBoolean());
        Assert.Equal(1, json.GetProperty("taken").GetInt32());
        Assert.Equal(
            "managedChange",
            json.GetProperty("differences")[0].GetProperty("kind").GetString()
        );
        Assert.Equal(0, json.GetProperty("blocking").GetArrayLength());
    }

    [Fact]
    public void TemplateStore_KeepsTheNewest_AndEveryRecordedOne_AndRefusesAChangedCopy()
    {
        string first = Layout(css: "body{first}");
        string firstHash = Managed.SaveTemplate(first, Now);
        Managed.Save(Live, new ObsManagedCollection(firstHash, ["x"], Now));
        List<string> later = Enumerable
            .Range(0, ObsManagedFiles.TemplatesKept + 2)
            .Select(i => Layout(css: "body{" + i + "}"))
            .ToList();
        for (int i = 0; i < later.Count; i++)
        {
            Managed.SaveTemplate(later[i], Now.AddMinutes(i + 1));
        }

        Assert.Equal(first, Managed.ReadTemplate(firstHash));
        Assert.Equal(
            ObsManagedFiles.TemplatesKept + 1,
            Directory.GetFiles(Managed.TemplateDirectory, "*.json").Length
        );
        Assert.Null(Managed.ReadTemplate(ObsCollectionPatcher.HashOf(later[0])));
        Assert.Equal(later[^1], Managed.ReadTemplate(ObsCollectionPatcher.HashOf(later[^1])));

        File.WriteAllText(Path.Combine(Managed.TemplateDirectory, firstHash + ".json"), "{}");
        Assert.Null(Managed.ReadTemplate(firstHash));
    }

    private ObsApplyResult Apply(bool write, bool previous, bool obsIsRunning = false) =>
        ObsCollectionApply.Run(
            new ObsApplyRequest
            {
                TemplatePath = TemplateOf(Install),
                PreviousTemplatePath = previous ? TemplateOf(Previous) : null,
                CollectionPath = Live,
                DataDirectory = @"C:\heroesreplay\Data",
                Managed = Managed,
                ObsIsRunning = obsIsRunning,
                Write = write,
                UtcNow = Now,
            }
        );

    private ObsManagedCollection RecordLiveFrom(string install)
    {
        var record = new ObsManagedCollection(
            TemplateHash(install),
            ObsCollectionPaths
                .SourceNames(File.ReadAllText(TemplateOf(install)))
                .Order(StringComparer.Ordinal)
                .ToList(),
            Now.AddDays(-1)
        );
        Managed.Save(Live, record);
        return record;
    }

    private static string TemplateOf(string install) =>
        Path.Combine(install, "obs", "Default.json");

    private static string TemplateHash(string install) =>
        ObsCollectionPatcher.TemplateHash(TemplateOf(install));

    private static void WriteTemplate(string install, string json)
    {
        Directory.CreateDirectory(Path.Combine(install, "obs"));
        File.WriteAllText(TemplateOf(install), json);
    }

    private Dictionary<string, byte[]> Snapshot() =>
        Directory
            .GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

    private void AssertUnchanged(Dictionary<string, byte[]> before)
    {
        Dictionary<string, byte[]> after = Snapshot();
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        Assert.All(before, file => Assert.Equal(file.Value, after[file.Key]));
    }
}
