using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.Obs.Collection;
using Xunit;
using static HeroesReplay.Tests.Unit.Obs.Collection.ObsCollectionFixture;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

/// <summary>#307: <c>obs plan</c> is read-only and says what an update would change.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ObsCollectionPlanTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-plan-" + Path.GetRandomFileName()
    );

    public ObsCollectionPlanTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Live));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Live =>
        Path.Combine(root, "appdata", "obs-studio", "basic", "scenes", "HeroesReplay.json");

    private string Install => Path.Combine(root, "app");

    private string Previous => Path.Combine(root, "app.previous");

    private ObsManagedFiles Managed => new(Path.Combine(root, "managed"));

    [Fact]
    public void Plan_WritesNothing_WhetherOBSRunsOrNot()
    {
        WriteTemplate(Previous, Layout(css: "body{}"));
        WriteTemplate(Install, Layout(css: "body{margin:0}"));
        File.WriteAllText(Live, Layout(css: "body{}", rankX: 1700));
        RecordLiveFrom(Previous);
        Dictionary<string, byte[]> before = Snapshot();

        ObsCollectionPlanResult running = Plan(obsIsRunning: true, previous: true);
        ObsCollectionPlanResult closed = Plan(obsIsRunning: false, previous: true);

        Assert.Equal("replace", running.Update.Action);
        Assert.True(running.Update.Deferred);
        Assert.True(running.Update.LiveSwap);
        Assert.Equal("replace", closed.Update.Action);
        Assert.False(closed.Update.Deferred);
        Dictionary<string, byte[]> after = Snapshot();
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        Assert.All(before, file => Assert.Equal(file.Value, after[file.Key]));
        Assert.DoesNotContain(
            Directory.GetDirectories(Path.GetTempPath(), "heroesreplay-obs-plan-*"),
            folder => Directory.GetCreationTimeUtc(folder) > DateTime.UtcNow.AddMinutes(-1)
        );
    }

    [Fact]
    public void Plan_AttributesEachDifference_FromThePreviousTemplate()
    {
        WriteTemplate(Previous, Layout(css: "body{}"));
        WriteTemplate(Install, Layout(css: "body{margin:0}"));
        File.WriteAllText(Live, Layout(css: "body{}", rankX: 1700));
        RecordLiveFrom(Previous);

        ObsCollectionPlanResult plan = Plan(obsIsRunning: false, previous: true);

        Assert.True(plan.Ok, plan.Message);
        Assert.Equal(ObsPlanCodes.Changes, plan.Code);
        Assert.Equal("previous", plan.Base);
        Assert.Equal(1, plan.Summary["managedChange"]);
        Assert.Equal(1, plan.Summary["operatorOverride"]);
        Assert.Equal(0, plan.Summary["conflict"]);
        Assert.Contains("replaces the whole collection", plan.Message);
    }

    [Fact]
    public void Plan_Json_IsAStableEnvelope()
    {
        WriteTemplate(Install, Layout());
        File.WriteAllText(Live, Layout(rankX: 1700));
        RecordLiveFrom(Install);

        ObsCollectionPlanResult plan = Plan(obsIsRunning: true);
        string json = plan.ToJson();

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement rootElement = document.RootElement;
        Assert.Equal(1, rootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(rootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(ObsPlanCodes.Changes, rootElement.GetProperty("code").GetString());
        Assert.Equal("install", rootElement.GetProperty("base").GetString());
        Assert.Equal("none", rootElement.GetProperty("update").GetProperty("action").GetString());
        JsonElement difference = rootElement.GetProperty("differences")[0];
        Assert.Equal("operatorOverride", difference.GetProperty("kind").GetString());
        Assert.Equal("sceneItem", difference.GetProperty("entity").GetString());
        Assert.Equal("keep", difference.GetProperty("apply").GetString());
        Assert.Equal(
            [
                "managedChange",
                "managedAddition",
                "managedRemoval",
                "operatorOverride",
                "operatorAddition",
                "operatorRemoval",
                "conflict",
                "unattributed",
            ],
            rootElement.GetProperty("summary").EnumerateObject().Select(p => p.Name)
        );
        ObsCollectionPlanResult read = ObsCollectionPlanResult.FromJson(json);
        Assert.Equal(plan.Code, read.Code);
        Assert.Equal(plan.Differences.Single(), read.Differences.Single());
    }

    [Fact]
    public void Plan_InSync_WhenTheLiveCollectionIsTheTemplate()
    {
        WriteTemplate(Install, Layout());
        File.WriteAllText(Live, Layout());
        RecordLiveFrom(Install);

        ObsCollectionPlanResult plan = Plan(obsIsRunning: false);

        Assert.True(plan.Ok);
        Assert.Equal(ObsPlanCodes.InSync, plan.Code);
        Assert.Equal("none", plan.Update.Action);
        Assert.Empty(plan.Differences);
    }

    [Fact]
    public void Plan_Conflict_IsNotOk()
    {
        WriteTemplate(Previous, Layout(cropTop: "0"));
        WriteTemplate(Install, Layout(cropTop: "20"));
        File.WriteAllText(Live, Layout(cropTop: "10"));
        RecordLiveFrom(Previous);

        ObsCollectionPlanResult plan = Plan(obsIsRunning: false, previous: true);

        Assert.False(plan.Ok);
        Assert.Equal(ObsPlanCodes.Conflict, plan.Code);
        Assert.Equal(ObsDiffApply.Refuse, plan.Differences.Single().Apply);
    }

    [Fact]
    public void Plan_CustomCollection_IsKeptByTheUpdate_AndNamesTheExtraSource()
    {
        WriteTemplate(Install, Layout());
        File.WriteAllText(Live, Layout(extraSources: [Source("my-webcam", "dshow_input")]));
        RecordLiveFrom(Install);

        ObsCollectionPlanResult plan = Plan(obsIsRunning: false);

        Assert.True(plan.Ok);
        Assert.Equal(ObsPlanCodes.Custom, plan.Code);
        Assert.Equal("keep", plan.Update.Action);
        Assert.Contains("my-webcam", plan.Update.Message);
        Assert.Equal(ObsDiffKind.OperatorAddition, plan.Differences.Single().Kind);
    }

    [Fact]
    public void Plan_OperatorAddedSource_SaysTheUpdateMerges_FromTheStoredBase()
    {
        string old = Layout(css: "body{}");
        WriteTemplate(Install, Layout(css: "body{margin:0}"));
        File.WriteAllText(
            Live,
            Layout(css: "body{}", extraSources: [Source("my-webcam", "dshow_input")])
        );
        Managed.SaveTemplate(old, DateTime.UtcNow);
        Managed.Save(
            Live,
            new ObsManagedCollection(
                ObsCollectionPatcher.HashOf(old),
                ObsCollectionPaths.SourceNames(old).Order(StringComparer.Ordinal).ToList(),
                DateTime.UtcNow
            )
        );
        Dictionary<string, byte[]> before = Snapshot();

        ObsCollectionPlanResult running = Plan(obsIsRunning: true);
        ObsCollectionPlanResult closed = Plan(obsIsRunning: false);

        Assert.Equal("stored", closed.Base);
        Assert.Equal(ObsPlanCodes.Changes, closed.Code);
        Assert.Equal("merge", closed.Update.Action);
        Assert.False(closed.Update.Deferred);
        Assert.Contains("keeps the operator's additions", closed.Message);
        Assert.Equal("merge", running.Update.Action);
        Assert.True(running.Update.Deferred);
        Assert.False(running.Update.LiveSwap);
        Dictionary<string, byte[]> after = Snapshot();
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        Assert.All(before, file => Assert.Equal(file.Value, after[file.Key]));
    }

    [Fact]
    public void Plan_WithoutACollection_SaysTheUpdateCreatesIt()
    {
        WriteTemplate(Install, Layout());

        ObsCollectionPlanResult plan = Plan(obsIsRunning: false);

        Assert.True(plan.Ok);
        Assert.Equal(ObsPlanCodes.Missing, plan.Code);
        Assert.Equal("create", plan.Update.Action);
        Assert.False(File.Exists(Live));
    }

    [Fact]
    public void Plan_WithoutATemplate_IsNotOk()
    {
        ObsCollectionPlanResult plan = Plan(obsIsRunning: false);

        Assert.False(plan.Ok);
        Assert.Equal(ObsPlanCodes.TemplateMissing, plan.Code);
    }

    [Fact]
    public void Plan_UnknownBase_LeavesDifferencesUnattributed()
    {
        WriteTemplate(Install, Layout(css: "body{margin:0}"));
        File.WriteAllText(Live, Layout(css: "body{}"));
        Managed.Save(
            Live,
            new ObsManagedCollection(ObsCollectionPatcher.UnknownTemplate, ["x"], DateTime.UtcNow)
        );

        ObsCollectionPlanResult plan = Plan(obsIsRunning: false);

        Assert.True(plan.Ok);
        Assert.Equal(ObsPlanCodes.BaseUnknown, plan.Code);
        Assert.Equal("none", plan.Base);
        Assert.Equal(ObsDiffKind.Unattributed, plan.Differences.Single().Kind);
    }

    [Fact]
    public void Plan_UnreadableCollection_IsNotOk()
    {
        WriteTemplate(Install, Layout());
        File.WriteAllText(Live, "{ not json");

        ObsCollectionPlanResult plan = Plan(obsIsRunning: false);

        Assert.False(plan.Ok);
        Assert.Equal(ObsPlanCodes.Unreadable, plan.Code);
        Assert.Equal("keep", plan.Update.Action);
        Assert.Equal("{ not json", File.ReadAllText(Live));
    }

    [Fact]
    public void Plan_ShowsARollbackThatWaits_AndTheUpdateThatFinishesIt()
    {
        WriteTemplate(Install, Layout(css: "body{}"));
        File.WriteAllText(Live, Layout(css: "body{margin:0}"));
        string backup = Path.Combine(root, "managed", "backups", "scenes-HeroesReplay.json.bak");
        Directory.CreateDirectory(Path.GetDirectoryName(backup));
        File.WriteAllText(backup, Layout(css: "body{}"));
        string hash = ObsCollectionPatcher.TemplateHash(
            Path.Combine(Install, "obs", "Default.json")
        );
        Managed.Save(Live, new ObsManagedCollection("FAILED", ["x"], DateTime.UtcNow));
        Managed.SavePendingRestore(
            new ObsPendingRestore
            {
                CollectionPath = Live,
                Backup = backup,
                TemplateSha256 = hash,
                Record = new ObsManagedCollection(hash, ["x"], DateTime.UtcNow),
                Reason = "OBS did not answer.",
            }
        );

        ObsCollectionPlanResult plan = Plan(obsIsRunning: true);

        Assert.Equal("restore", plan.Update.Action);
        Assert.True(plan.Update.LiveSwap);
        Assert.Contains(backup, plan.PendingRollback);
        Assert.NotNull(Managed.ReadPendingRestore());
        Assert.Contains("body{margin:0}", File.ReadAllText(Live));
    }

    private ObsCollectionPlanResult Plan(bool obsIsRunning, bool previous = false) =>
        ObsCollectionPlan.Build(
            new ObsCollectionPlanRequest
            {
                TemplatePath = Path.Combine(Install, "obs", "Default.json"),
                PreviousTemplatePath = previous
                    ? Path.Combine(Previous, "obs", "Default.json")
                    : null,
                CollectionPath = Live,
                CollectionName = "HeroesReplay",
                DataDirectory = @"C:\heroesreplay\Data",
                Managed = Managed,
                ObsIsRunning = obsIsRunning,
            }
        );

    /// <summary>The live collection was last written from that install's template.</summary>
    private void RecordLiveFrom(string install)
    {
        string template = Path.Combine(install, "obs", "Default.json");
        Managed.Save(
            Live,
            new ObsManagedCollection(
                ObsCollectionPatcher.TemplateHash(template),
                ObsCollectionPaths
                    .SourceNames(File.ReadAllText(template))
                    .Order(StringComparer.Ordinal)
                    .ToList(),
                DateTime.UtcNow
            )
        );
    }

    private static void WriteTemplate(string install, string json)
    {
        Directory.CreateDirectory(Path.Combine(install, "obs"));
        File.WriteAllText(Path.Combine(install, "obs", "Default.json"), json);
    }

    /// <summary>Every file under the test root, with its bytes.</summary>
    private Dictionary<string, byte[]> Snapshot() =>
        Directory
            .GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
}
