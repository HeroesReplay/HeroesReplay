using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsContractTests
{
    [Fact]
    public void From_Settings_NamesTheScenesSourcesAndPlacement()
    {
        OBSSettings settings = FakeObs.Settings();
        settings.ReportScenes = settings.ReportScenes.Append(
            new ReportScene
            {
                Enabled = false,
                SceneName = "disabled-scene",
                SourceName = "disabled-browser",
            }
        );

        ObsContract contract = ObsContract.From(settings);

        Assert.Equal(
            new[]
            {
                "game-scene",
                "waiting-screen",
                "match-report",
                "prediction-report",
                "request-queue",
            },
            contract.Scenes
        );
        Assert.Contains("current-replay", contract.Sources);
        Assert.Contains("tier-division", contract.Sources);
        Assert.Contains("rank-points", contract.Sources);
        Assert.Contains("grandmaster-image", contract.Sources);
        Assert.Contains("request-queue-browser", contract.Sources);
        Assert.DoesNotContain("disabled-browser", contract.Sources);
        Assert.Contains(new ObsContractItem("game-scene", "bronze-image"), contract.Items);
        Assert.Contains(
            new ObsContractItem("match-report", "match-report-browser"),
            contract.Items
        );
        Assert.DoesNotContain(contract.Items, item => item.Scene == "waiting-screen");
    }

    [Fact]
    public void From_NoRankList_UsesTheControllerDefault()
    {
        OBSSettings settings = FakeObs.Settings();
        settings.RankImagesSourceNames = null;

        Assert.Equal(
            RankImage.SourceNames.Length,
            ObsContract.From(settings).Sources.Count(name => name.EndsWith("-image"))
        );
    }

    [Fact]
    public void From_Null_IsEmpty()
    {
        ObsContract contract = ObsContract.From(null);

        Assert.Empty(contract.Scenes);
        Assert.Empty(contract.Sources);
        Assert.Empty(contract.Items);
    }

    [Fact]
    public void PackagedCollection_MeetsTheContract()
    {
        string json = File.ReadAllText(Path.Combine(FakeObs.RepoObsDirectory(), "Default.json"));
        ObsContract contract = ObsContract.From(FakeObs.Settings());

        Assert.Empty(ObsCollectionPaths.MissingScenes(json, contract.Scenes));
        Assert.Empty(ObsCollectionPaths.MissingSources(json, contract.Sources));
        Assert.Equal(
            "browser_source",
            ObsCollectionPaths.SourceKinds(json)["match-report-browser"]
        );
        Assert.Equal("scene", ObsCollectionPaths.SourceKinds(json)["game-scene"]);
    }

    [Fact]
    public void Drift_NamesExtraAndMissingSources()
    {
        ObsNameDrift same = ObsCollectionPatcher.Drift(new[] { "a", "b" }, new[] { "b", "a" });
        ObsNameDrift drift = ObsCollectionPatcher.Drift(new[] { "a", "b" }, new[] { "b", "c" });

        Assert.False(same.Custom);
        Assert.True(drift.Custom);
        Assert.Equal(new[] { "c" }, drift.Extra);
        Assert.Equal(new[] { "a" }, drift.Missing);
    }
}
