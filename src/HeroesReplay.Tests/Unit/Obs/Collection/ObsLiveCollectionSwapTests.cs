using System;
using System.IO;
using HeroesReplay.Core.Obs.Collection;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ObsLiveCollectionSwapTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-swap-" + Path.GetRandomFileName()
    );

    private string Scenes => Path.Combine(root, "scenes");

    private string MainFile => Path.Combine(Scenes, "HeroesReplay.json");

    public ObsLiveCollectionSwapTests()
    {
        Directory.CreateDirectory(Scenes);
        File.WriteAllText(MainFile, Collection("HeroesReplay", "old-layout"));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void Run_SwitchesThroughTheSpare_AndLeavesTheNewLayoutUnderTheMainName()
    {
        var obs = new FakeCollectionSwitch(Scenes, "HeroesReplay", "HeroesReplay-next");
        File.WriteAllText(
            Path.Combine(Scenes, "HeroesReplaynext.json"),
            Collection("HeroesReplay-next", "stale")
        );
        obs.Scene = "request-queue";

        ObsLiveSwapResult result = Swap(obs);

        Assert.True(result.Swapped, result.Message);
        Assert.Equal(["HeroesReplay-next", "HeroesReplay"], obs.Selected);
        Assert.Equal("HeroesReplay", obs.Current);
        Assert.Contains("new-layout", obs.Loaded);
        Assert.Contains("new-layout", File.ReadAllText(MainFile));
        Assert.Contains("\"name\":\"HeroesReplay\"", File.ReadAllText(MainFile).Replace(" ", ""));
        Assert.Equal(Collection("HeroesReplay", "old-layout"), File.ReadAllText(result.Backup));
        Assert.Equal("request-queue", obs.Scene);
        Assert.Equal("ABC", Managed().Read(MainFile).TemplateSha256);
    }

    [Fact]
    public void Run_RegistersTheSpareOnce_WhenOBSDoesNotListIt()
    {
        var obs = new FakeCollectionSwitch(Scenes, "HeroesReplay");

        ObsLiveSwapResult result = Swap(obs);

        Assert.True(result.Swapped, result.Message);
        Assert.Equal(["HeroesReplay-next"], obs.Created);
        Assert.Equal(["HeroesReplay", "HeroesReplay-next", "HeroesReplay"], obs.Selected);
        Assert.Contains("new-layout", File.ReadAllText(MainFile));
    }

    [Fact]
    public void Run_AnotherActiveCollection_IsLeftAlone()
    {
        var obs = new FakeCollectionSwitch(Scenes, "Streaming-Custom", "HeroesReplay");
        File.WriteAllText(
            Path.Combine(Scenes, "StreamingCustom.json"),
            Collection("Streaming-Custom", "operator")
        );

        ObsLiveSwapResult result = Swap(obs);

        Assert.False(result.Swapped);
        Assert.Empty(obs.Selected);
        Assert.Contains("old-layout", File.ReadAllText(MainFile));
    }

    [Fact]
    public void Run_FailedSwitchBack_ReportsTheSpareStillActive()
    {
        var obs = new FakeCollectionSwitch(Scenes, "HeroesReplay", "HeroesReplay-next")
        {
            FailSelect = "HeroesReplay",
        };
        File.WriteAllText(
            Path.Combine(Scenes, "HeroesReplaynext.json"),
            Collection("HeroesReplay-next", "stale")
        );

        ObsLiveSwapResult result = Swap(obs);

        Assert.False(result.Swapped);
        Assert.True(result.Stranded);
        Assert.Equal("HeroesReplay-next", obs.Current);
        Assert.Equal(ObsLiveCollectionSwap.ReturnAttempts, obs.FailedSelects);
    }

    /// <summary>#214: the first swap creates an empty spare, and the switch back throws.</summary>
    [Fact]
    public void Run_FailedSwitchBackAfterCreatingTheSpare_ReportsStrandedInsteadOfThrowing()
    {
        var obs = new FakeCollectionSwitch(Scenes, "HeroesReplay") { FailSelect = "HeroesReplay" };

        ObsLiveSwapResult result = Swap(obs);

        Assert.True(result.Stranded);
        Assert.False(result.Swapped);
        Assert.Equal("HeroesReplay-next", obs.Current);
        Assert.Contains("left 'HeroesReplay'", result.Message);
    }

    [Fact]
    public void Run_RetriesTheSwitchBack_AndEndsOnTheMainCollection()
    {
        var obs = new FakeCollectionSwitch(Scenes, "HeroesReplay")
        {
            FailSelect = "HeroesReplay",
            FailSelectTimes = 1,
        };

        ObsLiveSwapResult result = Swap(obs);

        Assert.False(result.Stranded);
        Assert.Equal("HeroesReplay", obs.Current);
    }

    /// <summary>#214: a later session switches back even though the template record already matches.</summary>
    [Fact]
    public void Recover_SwitchesAStrandedOBSBackToTheMainCollection()
    {
        File.WriteAllText(
            Path.Combine(Scenes, "HeroesReplaynext.json"),
            Collection("HeroesReplay-next", "new-layout")
        );
        var onSpare = new FakeCollectionSwitch(Scenes, "HeroesReplay-next", "HeroesReplay");
        var onMain = new FakeCollectionSwitch(Scenes, "HeroesReplay", "HeroesReplay-next");

        ObsLiveSwapResult recovered = ObsLiveCollectionSwap.Recover(
            onSpare,
            "HeroesReplay",
            _ => { }
        );
        ObsLiveSwapResult untouched = ObsLiveCollectionSwap.Recover(
            onMain,
            "HeroesReplay",
            _ => { }
        );

        Assert.False(recovered.Stranded);
        Assert.Equal("HeroesReplay", onSpare.Current);
        Assert.Null(untouched);
        Assert.Empty(onMain.Selected);
    }

    [Fact]
    public void CollectionFile_FindsTheFileByTheNameInside()
    {
        File.WriteAllText(
            Path.Combine(Scenes, "HeroesReplaynext.json"),
            Collection("HeroesReplay-next", "x")
        );
        File.WriteAllText(Path.Combine(Scenes, "broken.json"), "{ not json");

        Assert.Equal(
            Path.Combine(Scenes, "HeroesReplaynext.json"),
            ObsLiveCollectionSwap.CollectionFile(Scenes, "HeroesReplay-next")
        );
        Assert.Null(ObsLiveCollectionSwap.CollectionFile(Scenes, "missing"));
    }

    private ObsLiveSwapResult Swap(FakeCollectionSwitch obs) =>
        ObsLiveCollectionSwap.Run(
            obs,
            new ObsCollectionReplacement(
                MainFile,
                Collection("HeroesReplay", "new-layout"),
                "ABC",
                ["new-layout"]
            ),
            "HeroesReplay",
            Managed(),
            new DateTime(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc),
            _ => { }
        );

    private ObsManagedFiles Managed() => new(Path.Combine(root, "managed"));

    private static string Collection(string name, string source) =>
        $$"""{ "name": "{{name}}", "sources": [ { "name": "{{source}}" } ] }""";
}
