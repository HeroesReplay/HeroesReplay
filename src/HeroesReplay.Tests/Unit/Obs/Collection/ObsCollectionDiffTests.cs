using System.Linq;
using HeroesReplay.Core.Obs.Collection;
using Xunit;
using static HeroesReplay.Tests.Unit.Obs.Collection.ObsCollectionFixture;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

/// <summary>#307: the three-way, property-by-property collection diff.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ObsCollectionDiffTests
{
    [Fact]
    public void SameLayout_HasNoDifferences_WhateverIdsOBSAssigned()
    {
        // Every Layout() call gives new uuids; OBS numbers and orders by itself too.
        string live = Layout()
            .Replace("\"volume\":1.0", "\"volume\":1", System.StringComparison.Ordinal)
            .Replace("\"id\":29", "\"id\":41", System.StringComparison.Ordinal);

        ObsCollectionDiffResult diff = ObsCollectionDiff.Compare(Layout(), Layout(), live);

        Assert.True(diff.BaseKnown);
        Assert.Empty(diff.Differences);
    }

    [Fact]
    public void OperatorAddedSource_IsPreserved_AndReportedOnce()
    {
        string webcam = Source("my-webcam", "dshow_input", "{\"video_device_id\":\"cam\"}");
        string live = Document(
            Source(
                "match-report-browser",
                settings: "{\"url\":\"file:///C:/heroesreplay/Data/match-report.html\",\"css\":\"body{}\",\"width\":1920}"
            ),
            Source(
                "countdown",
                settings: "{\"width\":1920}",
                filters: [Filter("Crop/Pad", settings: "{\"top\":0}")]
            ),
            Source(
                "rank-image",
                "image_source",
                "{\"file\":\"C:/heroesreplay/app/obs/images/rank.png\"}"
            ),
            webcam,
            Scene(
                "game-scene",
                Item("rank-image", 1799, 0, id: 29),
                Item("countdown", id: 3),
                Item("my-webcam", 10, 10, id: 30)
            ),
            Scene("match-report", Item("match-report-browser", id: 1))
        );

        ObsCollectionDiffResult diff = ObsCollectionDiff.Compare(Layout(), Layout(), live);

        ObsCollectionDifference added = Assert.Single(diff.Differences);
        Assert.Equal(ObsDiffKind.OperatorAddition, added.Kind);
        Assert.Equal(ObsDiffEntity.Source, added.Entity);
        Assert.Equal("my-webcam", added.Source);
        Assert.Null(added.Property);
        Assert.Equal(ObsDiffApply.Keep, added.Apply);
    }

    [Fact]
    public void ChangedManagedSetting_IsAManagedChange_TheMergeTakes()
    {
        ObsCollectionDiffResult diff = ObsCollectionDiff.Compare(
            Layout(css: "body{}"),
            Layout(css: "body{margin:0}"),
            Layout(css: "body{}")
        );

        ObsCollectionDifference change = Assert.Single(diff.Differences);
        Assert.Equal(ObsDiffKind.ManagedChange, change.Kind);
        Assert.Equal("match-report-browser", change.Source);
        Assert.Equal("settings.css", change.Property);
        Assert.Equal("\"body{}\"", change.Base);
        Assert.Equal("\"body{margin:0}\"", change.Template);
        Assert.Equal("\"body{}\"", change.Live);
        Assert.Equal(ObsDiffApply.Template, change.Apply);
        Assert.False(diff.HasConflicts);
    }

    [Fact]
    public void OperatorChange_WhereTheTemplateDidNot_IsAnOverride_TheMergeKeeps()
    {
        ObsCollectionDiffResult diff = ObsCollectionDiff.Compare(
            Layout(rankX: 1799),
            Layout(rankX: 1799),
            Layout(rankX: 1700)
        );

        ObsCollectionDifference moved = Assert.Single(diff.Differences);
        Assert.Equal(ObsDiffKind.OperatorOverride, moved.Kind);
        Assert.Equal(ObsDiffEntity.SceneItem, moved.Entity);
        Assert.Equal("game-scene", moved.Scene);
        Assert.Equal("rank-image", moved.Source);
        Assert.Equal("pos.x", moved.Property);
        Assert.Equal("1700", moved.Live);
        Assert.Equal(ObsDiffApply.Keep, moved.Apply);
    }

    [Fact]
    public void BothChangedTheSameValue_IsAConflict_TheMergeRefuses()
    {
        ObsCollectionDiffResult diff = ObsCollectionDiff.Compare(
            Layout(cropTop: "0"),
            Layout(cropTop: "20"),
            Layout(cropTop: "10")
        );

        ObsCollectionDifference conflict = Assert.Single(diff.Differences);
        Assert.Equal(ObsDiffKind.Conflict, conflict.Kind);
        Assert.Equal(ObsDiffEntity.Filter, conflict.Entity);
        Assert.Equal("countdown", conflict.Source);
        Assert.Equal("Crop/Pad", conflict.Filter);
        Assert.Equal("settings.top", conflict.Property);
        Assert.Equal(ObsDiffApply.Refuse, conflict.Apply);
        Assert.True(diff.HasConflicts);
    }

    [Fact]
    public void ManagedSourceTheOperatorRemoved_IsReported_NeverReAdded()
    {
        string live = Document(
            Source(
                "match-report-browser",
                settings: "{\"url\":\"file:///C:/heroesreplay/Data/match-report.html\",\"css\":\"body{}\",\"width\":1920}"
            ),
            Source(
                "rank-image",
                "image_source",
                "{\"file\":\"C:/heroesreplay/app/obs/images/rank.png\"}"
            ),
            Scene("game-scene", Item("rank-image", 1799, 0, id: 29)),
            Scene("match-report", Item("match-report-browser", id: 1))
        );

        ObsCollectionDiffResult diff = ObsCollectionDiff.Compare(Layout(), Layout(), live);

        // One line for the source: its filter and its scene item go with it.
        ObsCollectionDifference removed = Assert.Single(diff.Differences);
        Assert.Equal(ObsDiffKind.OperatorRemoval, removed.Kind);
        Assert.Equal("countdown", removed.Source);
        Assert.Equal(ObsDiffApply.Keep, removed.Apply);
    }

    [Fact]
    public void TemplateAdditionsAndRemovals_AreManaged_UnlessTheOperatorChangedThem()
    {
        string clip = Source("clip-player", "ffmpeg_source", "{\"local_file\":\"clip.mp4\"}");
        ObsCollectionDiffResult added = ObsCollectionDiff.Compare(
            Layout(),
            Layout(extraSources: [clip]),
            Layout()
        );
        ObsCollectionDifference addition = Assert.Single(added.Differences);
        Assert.Equal(ObsDiffKind.ManagedAddition, addition.Kind);
        Assert.Equal("clip-player", addition.Source);
        Assert.Equal(ObsDiffApply.Template, addition.Apply);

        // The next template drops it: unchanged in the live collection, the merge removes it.
        string liveClip = Source("clip-player", "ffmpeg_source", "{\"local_file\":\"clip.mp4\"}");
        ObsCollectionDiffResult removed = ObsCollectionDiff.Compare(
            Layout(extraSources: [clip]),
            Layout(),
            Layout(extraSources: [liveClip])
        );
        ObsCollectionDifference removal = Assert.Single(removed.Differences);
        Assert.Equal(ObsDiffKind.ManagedRemoval, removal.Kind);
        Assert.Equal(ObsDiffApply.Template, removal.Apply);

        // The operator changed it before the template dropped it: both changed it.
        string changedClip = Source(
            "clip-player",
            "ffmpeg_source",
            "{\"local_file\":\"mine.mp4\"}"
        );
        ObsCollectionDiffResult conflict = ObsCollectionDiff.Compare(
            Layout(extraSources: [clip]),
            Layout(),
            Layout(extraSources: [changedClip])
        );
        Assert.Equal(ObsDiffKind.Conflict, Assert.Single(conflict.Differences).Kind);
    }

    [Fact]
    public void FilterSwitchedOffByTheOperator_IsAnOverride()
    {
        ObsCollectionDiffResult diff = ObsCollectionDiff.Compare(
            Layout(),
            Layout(),
            Layout(cropEnabled: false)
        );

        ObsCollectionDifference off = Assert.Single(diff.Differences);
        Assert.Equal(ObsDiffKind.OperatorOverride, off.Kind);
        Assert.Equal("Crop/Pad", off.Filter);
        Assert.Equal("enabled", off.Property);
        Assert.Equal("false", off.Live);
    }

    [Fact]
    public void WithoutABase_DifferencesAreUnattributed_AndKept()
    {
        ObsCollectionDiffResult diff = ObsCollectionDiff.Compare(
            null,
            Layout(css: "body{margin:0}"),
            Layout(css: "body{}")
        );

        Assert.False(diff.BaseKnown);
        ObsCollectionDifference unknown = Assert.Single(diff.Differences);
        Assert.Equal(ObsDiffKind.Unattributed, unknown.Kind);
        Assert.Null(unknown.Base);
        Assert.Equal(ObsDiffApply.Keep, unknown.Apply);
    }

    [Fact]
    public void ValuesTheSpectatorSets_AndOBSFloatRounding_AreNotDifferences()
    {
        var obs = new HeroesReplay.Core.Obs.OBSSettings
        {
            GameSceneName = "game-scene",
            RankImagesSourceNames = ["rank-image"],
            ReportScenes =
            [
                new HeroesReplay.Core.Obs.ReportScene
                {
                    Enabled = true,
                    SceneName = "match-report",
                    SourceName = "match-report-browser",
                },
            ],
        };
        // The spectator rewrote the match report css (its scroll) and hid the rank image,
        // and OBS saved a scale of 0.66 back at single precision.
        string template = Layout(css: "body{}")
            .Replace(
                "\"scale\":{\"x\":1.0",
                "\"scale\":{\"x\":0.66",
                System.StringComparison.Ordinal
            );
        string live = Layout(css: "body{} @keyframes scroll{}")
            .Replace(
                "\"visible\":true,\"id\":29",
                "\"visible\":false,\"id\":29",
                System.StringComparison.Ordinal
            )
            .Replace(
                "\"scale\":{\"x\":1.0",
                "\"scale\":{\"x\":0.6600000262260437",
                System.StringComparison.Ordinal
            );
        Assert.Contains("\"visible\":false,\"id\":29", live);

        ObsCollectionDiffResult withRuntime = ObsCollectionDiff.Compare(
            template,
            template,
            live,
            ObsRuntimeValues.From(obs)
        );
        ObsCollectionDiffResult without = ObsCollectionDiff.Compare(template, template, live);

        Assert.Empty(withRuntime.Differences);
        // Only the float rounding is never a difference.
        Assert.Equal(
            ["settings.css", "visible"],
            without.Differences.Select(difference => difference.Property)
        );
    }

    [Fact]
    public void Describe_NamesWhereAndWhat()
    {
        ObsCollectionDifference moved = ObsCollectionDiff
            .Compare(Layout(rankX: 1799), Layout(rankX: 1799), Layout(rankX: 1700))
            .Differences.Single();

        Assert.Equal(
            "'rank-image' in scene 'game-scene' pos.x: template 1799, live 1700, was 1799",
            moved.Describe()
        );
    }
}
