using System;
using System.Linq;
using System.Text.Json.Nodes;
using HeroesReplay.Core.Obs.Collection;
using Xunit;
using static HeroesReplay.Tests.Unit.Obs.Collection.ObsCollectionFixture;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

/// <summary>#307: the three-way merge behind <c>obs apply</c> and an update of a custom collection.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ObsCollectionMergeTests
{
    private static readonly string Webcam = Source(
        "my-webcam",
        "dshow_input",
        "{\"video_device_id\":\"cam\"}"
    );

    [Fact]
    public void ManagedChange_IsTaken_AndTheOperatorsSourceAndOverrideAreKept()
    {
        string live = Layout(css: "body{}", rankX: 1700, extraSources: [Webcam]);

        ObsMergeResult merge = Merge(
            Layout(css: "body{}"),
            Layout(css: "body{margin:0}"),
            live,
            ObsMergeScope.KeepOperatorChanges
        );

        Assert.True(merge.Ok, merge.Message);
        Assert.Equal(1, merge.Taken);
        Assert.Equal(2, merge.Kept);
        JsonObject merged = Parse(merge.Merged);
        Assert.Equal(
            "body{margin:0}",
            (string)SourceNamed(merged, "match-report-browser")["settings"]!["css"]
        );
        Assert.Equal(
            "cam",
            (string)SourceNamed(merged, "my-webcam")["settings"]!["video_device_id"]
        );
        Assert.Equal(1700, (double)ItemNamed(merged, "game-scene", "rank-image")["pos"]!["x"]);

        // OBS ids are the live collection's.
        Assert.Equal(
            (string)SourceNamed(Parse(live), "countdown")["uuid"],
            (string)SourceNamed(merged, "countdown")["uuid"]
        );
    }

    [Fact]
    public void AdditionsOnly_TakesTheTemplate_AndKeepsTheOperatorsSource()
    {
        ObsMergeResult merge = Merge(
            Layout(cropTop: "0"),
            Layout(cropTop: "20"),
            Layout(cropTop: "0", extraSources: [Webcam]),
            ObsMergeScope.AdditionsOnly
        );

        Assert.True(merge.Ok, merge.Message);
        JsonObject merged = Parse(merge.Merged);
        Assert.Equal(20, (int)Filters(merged, "countdown").Single()!["settings"]!["top"]);
        Assert.NotNull(SourceNamed(merged, "my-webcam"));
    }

    [Fact]
    public void AdditionsOnly_RefusesAnOperatorOverride_WhichObsApplyKeeps()
    {
        string based = Layout(css: "body{}");
        string template = Layout(css: "body{margin:0}");
        string live = Layout(css: "body{}", rankX: 1700, extraSources: [Webcam]);

        ObsMergeResult update = Merge(based, template, live, ObsMergeScope.AdditionsOnly);
        ObsMergeResult apply = Merge(based, template, live, ObsMergeScope.KeepOperatorChanges);

        Assert.Equal(ObsMergeOutcome.OperatorChanges, update.Outcome);
        Assert.Null(update.Merged);
        Assert.Equal("pos.x", Assert.Single(update.Blocking).Property);
        Assert.Contains("obs apply --backup", update.Message, StringComparison.Ordinal);
        Assert.True(apply.Ok, apply.Message);
    }

    [Fact]
    public void Conflict_Refuses_AndListsIt()
    {
        ObsMergeResult merge = Merge(
            Layout(cropTop: "0"),
            Layout(cropTop: "20"),
            Layout(cropTop: "10"),
            ObsMergeScope.KeepOperatorChanges
        );

        Assert.Equal(ObsMergeOutcome.Conflict, merge.Outcome);
        Assert.Null(merge.Merged);
        ObsCollectionDifference conflict = Assert.Single(merge.Blocking);
        Assert.Equal("Crop/Pad", conflict.Filter);
        Assert.Contains("Crop/Pad", merge.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutABase_Refuses()
    {
        ObsMergeResult merge = Merge(
            null,
            Layout(css: "body{margin:0}"),
            Layout(css: "body{}"),
            ObsMergeScope.KeepOperatorChanges
        );

        Assert.Equal(ObsMergeOutcome.BaseUnknown, merge.Outcome);
        Assert.Null(merge.Merged);
    }

    [Fact]
    public void NothingToTake_ReturnsTheLiveTextAsItIs()
    {
        string live = Layout(rankX: 1700, extraSources: [Webcam]);

        ObsMergeResult merge = Merge(Layout(), Layout(), live, ObsMergeScope.KeepOperatorChanges);

        Assert.True(merge.Ok);
        Assert.Equal(0, merge.Taken);
        Assert.Same(live, merge.Merged);
    }

    [Fact]
    public void TemplateAddsASource_ItsPlacementGoesIntoTheLiveScene_WithANewItemId()
    {
        string queue = Source("queue-browser", settings: "{\"width\":600}");
        string based = Document(
            Source("countdown"),
            Source("rank-image", "image_source"),
            Scene("game-scene", Item("rank-image", 10, 0, id: 29), Item("countdown", id: 3))
        );
        string template = Document(
            Source("countdown"),
            Source("rank-image", "image_source"),
            queue,
            Scene(
                "game-scene",
                Item("rank-image", 10, 0, id: 29),
                Item("queue-browser", 50, 60, id: 4),
                Item("countdown", id: 3)
            )
        );

        // The operator moved the countdown and placed a webcam on top.
        string live = Document(
            Source("countdown"),
            Source("rank-image", "image_source"),
            Webcam,
            Scene(
                "game-scene",
                Item("rank-image", 10, 0, id: 29),
                Item("countdown", 5, 5, id: 3),
                Item("my-webcam", id: 30)
            )
        );

        ObsMergeResult merge = Merge(based, template, live, ObsMergeScope.KeepOperatorChanges);

        Assert.True(merge.Ok, merge.Message);
        JsonObject merged = Parse(merge.Merged);
        Assert.Equal(
            ["rank-image", "queue-browser", "countdown", "my-webcam"],
            Items(merged, "game-scene").Select(item => (string)item!["name"])
        );
        JsonObject placed = ItemNamed(merged, "game-scene", "queue-browser");
        Assert.Equal(50, (double)placed["pos"]!["x"]);
        Assert.Equal(31, (long)placed["id"]);
        Assert.Equal(31, (long)SourceNamed(merged, "game-scene")["settings"]!["id_counter"]);
        Assert.Equal(
            (string)SourceNamed(merged, "queue-browser")["uuid"],
            (string)placed["source_uuid"]
        );
        Assert.Equal(5, (double)ItemNamed(merged, "game-scene", "countdown")["pos"]!["x"]);
    }

    [Fact]
    public void TemplateRemovesASource_ItAndItsPlacementsGo_ButAnOperatorRemovalIsNotUndone()
    {
        string based = Layout();
        string template = Document(
            Source(
                "match-report-browser",
                settings: "{\"url\":\"file:///C:/heroesreplay/Data/match-report.html\",\"css\":\"body{}\",\"width\":1920}"
            ),
            Source(
                "countdown",
                settings: "{\"width\":1920}",
                filters: [Filter("Crop/Pad", settings: "{\"top\":0}")]
            ),
            Scene("game-scene", Item("countdown", id: 3)),
            Scene("match-report", Item("match-report-browser", id: 1))
        );

        // The operator removed the match report scene the template still has.
        JsonObject liveDocument = Parse(Layout());
        JsonArray sources = liveDocument["sources"]!.AsArray();
        sources.Remove(sources.Single(source => (string)source!["name"] == "match-report"));
        string live = liveDocument.ToJsonString();

        ObsMergeResult merge = Merge(based, template, live, ObsMergeScope.KeepOperatorChanges);

        Assert.True(merge.Ok, merge.Message);
        JsonObject merged = Parse(merge.Merged);
        Assert.Null(SourceNamed(merged, "rank-image"));
        Assert.Equal(
            ["countdown"],
            Items(merged, "game-scene").Select(item => (string)item!["name"])
        );
        Assert.Null(SourceNamed(merged, "match-report"));
    }

    [Fact]
    public void TemplateAddsAFilter_ItGoesWhereTheTemplateHasIt_AndARemovedOneGoes()
    {
        string based = Document(
            Source("countdown", filters: [Filter("Crop/Pad"), Filter("Color", "color_filter")])
        );
        string template = Document(
            Source(
                "countdown",
                filters:
                [
                    Filter("Crop/Pad"),
                    Filter("Sharpen", "sharpness_filter", "{\"sharpness\":0.2}"),
                ]
            )
        );
        string live = Document(
            Source(
                "countdown",
                filters:
                [
                    Filter("Crop/Pad"),
                    Filter("Color", "color_filter"),
                    Filter("Mine", "gain_filter"),
                ]
            )
        );

        ObsMergeResult merge = Merge(based, template, live, ObsMergeScope.KeepOperatorChanges);

        Assert.True(merge.Ok, merge.Message);
        Assert.Equal(
            ["Crop/Pad", "Sharpen", "Mine"],
            Filters(Parse(merge.Merged), "countdown").Select(filter => (string)filter!["name"])
        );
    }

    [Fact]
    public void TemplateAddsAScene_ItGoesIntoTheSceneOrder()
    {
        const string order =
            "\"scene_order\":[{\"name\":\"game-scene\"},{\"name\":\"match-report\"}],\"sources\":[";
        string based = Layout().Replace("\"sources\":[", order, StringComparison.Ordinal);
        string template = Layout(extraSources: [Scene("intermission", Item("countdown", id: 1))])
            .Replace(
                "\"sources\":[",
                "\"scene_order\":[{\"name\":\"game-scene\"},{\"name\":\"intermission\"},{\"name\":\"match-report\"}],\"sources\":[",
                StringComparison.Ordinal
            );
        string live = Layout(extraSources: [Webcam])
            .Replace("\"sources\":[", order, StringComparison.Ordinal);

        ObsMergeResult merge = Merge(based, template, live, ObsMergeScope.AdditionsOnly);

        Assert.True(merge.Ok, merge.Message);
        JsonObject merged = Parse(merge.Merged);
        Assert.Equal(
            ["game-scene", "intermission", "match-report"],
            merged["scene_order"]!.AsArray().Select(scene => (string)scene!["name"])
        );
        Assert.Equal(
            (string)SourceNamed(merged, "countdown")["uuid"],
            (string)ItemNamed(merged, "intermission", "countdown")["source_uuid"]
        );
    }

    [Fact]
    public void TemplateAddsASetting_AndRemovesOne_InsideANestedObject()
    {
        string based = Document(
            Source("info", "text_gdiplus", "{\"font\":{\"face\":\"Arial\",\"size\":40}}")
        );
        string template = Document(
            Source("info", "text_gdiplus", "{\"font\":{\"face\":\"Arial\",\"style\":\"Bold\"}}")
        );
        string live = Document(
            Source(
                "info",
                "text_gdiplus",
                "{\"font\":{\"face\":\"Arial\",\"size\":40},\"color\":255}"
            )
        );

        ObsMergeResult merge = Merge(based, template, live, ObsMergeScope.AdditionsOnly);

        Assert.True(merge.Ok, merge.Message);
        JsonObject settings = SourceNamed(Parse(merge.Merged), "info")["settings"]!.AsObject();
        Assert.Equal("Bold", (string)settings["font"]!["style"]);
        Assert.False(settings["font"]!.AsObject().ContainsKey("size"));
        Assert.Equal(255, (int)settings["color"]);
    }

    /// <summary>The packaged collection itself, as a release would move it and an operator add to it.</summary>
    [Fact]
    public void PackagedCollection_TemplateAddsAndRemoves_OperatorAdds_MergesAndChecksOut()
    {
        string packaged = System.IO.File.ReadAllText(
            System.IO.Path.Combine(FakeObs.RepoObsDirectory(), "Default.json")
        );

        // The template places a new browser source on the waiting screen, moves the logo, and
        // drops the platinum image's filter.
        JsonObject template = Parse(packaged);
        JsonObject added = SourceNamed(template, "countdown").DeepClone().AsObject();
        added["name"] = "intermission-browser";
        added["uuid"] = Guid.NewGuid().ToString();
        added.Remove("filters");
        template["sources"]!.AsArray().Add(added);
        JsonObject placement = ItemNamed(template, "waiting-screen", "countdown")
            .DeepClone()
            .AsObject();
        placement["name"] = "intermission-browser";
        placement["source_uuid"] = (string)added["uuid"];
        placement["id"] = 46;
        Items(template, "waiting-screen").Add(placement);
        ItemNamed(template, "waiting-screen", "hots-logo")["pos"]!["x"] = 123.0;
        SourceNamed(template, "platinum-image")["filters"] = new JsonArray();

        // The operator placed a webcam on the game scene and added a filter to the game capture.
        JsonObject live = Parse(packaged);
        JsonObject webcam = Parse(Webcam);
        live["sources"]!.AsArray().Add(webcam);
        Items(live, "game-scene").Add(Parse(Item("my-webcam", 10, 10, id: 32)));
        SourceNamed(live, "game-capture")["filters"] = new JsonArray(
            Parse(Filter("Sharpen", "sharpness_filter", "{\"sharpness\":0.1}"))
        );

        ObsMergeResult merge = Merge(
            packaged,
            template.ToJsonString(),
            live.ToJsonString(),
            ObsMergeScope.AdditionsOnly
        );

        Assert.True(merge.Ok, merge.Message);
        Assert.Equal(3, merge.Taken);
        Assert.Equal(2, merge.Kept);
        JsonObject merged = Parse(merge.Merged);
        Assert.NotNull(SourceNamed(merged, "intermission-browser"));
        Assert.Equal(
            (string)SourceNamed(merged, "intermission-browser")["uuid"],
            (string)ItemNamed(merged, "waiting-screen", "intermission-browser")["source_uuid"]
        );
        Assert.Equal(123, (double)ItemNamed(merged, "waiting-screen", "hots-logo")["pos"]!["x"]);
        Assert.Empty(Filters(merged, "platinum-image"));
        Assert.NotNull(ItemNamed(merged, "game-scene", "my-webcam"));
        Assert.Equal("Sharpen", (string)Filters(merged, "game-capture").Single()!["name"]);
        Assert.Equal((string)live["current_scene"], (string)merged["current_scene"]);
    }

    private static ObsMergeResult Merge(
        string based,
        string template,
        string live,
        ObsMergeScope scope
    ) => ObsCollectionMerge.Merge(based, template, live, ObsRuntimeValues.None, scope);

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    private static JsonObject SourceNamed(JsonObject document, string name) =>
        document["sources"]!
            .AsArray()
            .OfType<JsonObject>()
            .FirstOrDefault(source => (string)source["name"] == name);

    private static JsonArray Items(JsonObject document, string scene) =>
        SourceNamed(document, scene)["settings"]!["items"]!.AsArray();

    private static JsonObject ItemNamed(JsonObject document, string scene, string source) =>
        Items(document, scene).OfType<JsonObject>().Single(item => (string)item["name"] == source);

    private static JsonArray Filters(JsonObject document, string source) =>
        SourceNamed(document, source)["filters"]!.AsArray();
}
