using System;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Obs;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsNamesTests
{
    [Fact]
    public void Settings_DefaultToHeroesReplay()
    {
        var obs = new OBSSettings();

        Assert.Equal("HeroesReplay", obs.ProfileName);
        Assert.Equal("HeroesReplay", obs.SceneCollectionName);
        Assert.Equal("HeroesReplay", ObsNames.Profile(null));
        Assert.Equal(
            "HeroesReplay",
            ObsNames.SceneCollection(new OBSSettings { SceneCollectionName = " " })
        );
        Assert.Equal(
            "HeroesReplay-live",
            ObsNames.Profile(new OBSSettings { ProfileName = " HeroesReplay-live " })
        );
    }

    [Fact]
    public void Paths_FollowTheConfiguredNames()
    {
        string appData = Path.Combine(Path.GetTempPath(), "hr-appdata");

        Assert.Equal(
            Path.Combine(
                appData,
                "obs-studio",
                "basic",
                "profiles",
                "HeroesReplay-dev",
                "basic.ini"
            ),
            ObsNames.ProfileIni(appData, "HeroesReplay-dev")
        );
        Assert.Equal(
            Path.Combine(appData, "obs-studio", "basic", "scenes", "HeroesReplay-dev.json"),
            ObsNames.CollectionFile(appData, "HeroesReplay-dev")
        );
        Assert.Equal(
            Path.Combine(appData, "obs-studio", "basic", "scenes", "HeroesReplay.json"),
            ObsCollectionPatcher.LiveCollectionPath(appData)
        );
    }

    [Fact]
    public void WithProfileName_RewritesOnlyTheGeneralName()
    {
        const string ini =
            "[General]\r\nName=HeroesReplay\r\n\r\n[Audio]\r\nMonitoringDeviceName=Default\r\nName=keep\r\n";

        string renamed = ObsNames.WithProfileName(ini, "HeroesReplay-live");

        Assert.Equal(
            "[General]\r\nName=HeroesReplay-live\r\n\r\n[Audio]\r\nMonitoringDeviceName=Default\r\nName=keep\r\n",
            renamed
        );
        Assert.Equal(ini, ObsNames.WithProfileName(ini, "HeroesReplay"));
        Assert.StartsWith(
            "[General]\r\nName=X\r\n",
            ObsNames.WithProfileName("[Video]\r\nBaseCX=1920\r\n", "X"),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void WithCollectionName_SetsTheTopLevelNameOnly()
    {
        const string json = """
            {
              "name": "HeroesReplay",
              "sources": [ { "name": "game-scene", "id": "scene" } ]
            }
            """;

        Assert.Same(json, ObsNames.WithCollectionName(json, "HeroesReplay"));

        string renamed = ObsNames.WithCollectionName(json, "HeroesReplay-live");
        using JsonDocument document = JsonDocument.Parse(renamed);
        Assert.Equal("HeroesReplay-live", document.RootElement.GetProperty("name").GetString());
        Assert.Equal(
            "game-scene",
            document.RootElement.GetProperty("sources")[0].GetProperty("name").GetString()
        );
    }
}
