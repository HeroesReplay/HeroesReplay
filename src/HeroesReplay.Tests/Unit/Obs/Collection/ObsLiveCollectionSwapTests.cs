using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        var obs = new FakeObs(Scenes, "HeroesReplay", "HeroesReplay-next");
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
        var obs = new FakeObs(Scenes, "HeroesReplay");

        ObsLiveSwapResult result = Swap(obs);

        Assert.True(result.Swapped, result.Message);
        Assert.Equal(["HeroesReplay-next"], obs.Created);
        Assert.Equal(["HeroesReplay", "HeroesReplay-next", "HeroesReplay"], obs.Selected);
        Assert.Contains("new-layout", File.ReadAllText(MainFile));
    }

    [Fact]
    public void Run_AnotherActiveCollection_IsLeftAlone()
    {
        var obs = new FakeObs(Scenes, "Streaming-Custom", "HeroesReplay");
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
        var obs = new FakeObs(Scenes, "HeroesReplay", "HeroesReplay-next")
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

    private ObsLiveSwapResult Swap(FakeObs obs) =>
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
            new DateTime(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc)
        );

    private ObsManagedFiles Managed() => new(Path.Combine(root, "managed"));

    private static string Collection(string name, string source) =>
        $$"""{ "name": "{{name}}", "sources": [ { "name": "{{source}}" } ] }""";

    /// <summary>
    /// OBS as measured on ASA-SERVER: it lists the collections it started with or created, saves
    /// the active one to its file when it switches away, and reads the next one from its file.
    /// </summary>
    private sealed class FakeObs : IObsCollectionSwitch
    {
        private readonly string scenes;
        private readonly List<string> collections;
        private string memory;

        public FakeObs(string scenes, string current, params string[] others)
        {
            this.scenes = scenes;
            Current = current;
            collections = [current, .. others];
            memory = File.Exists(FileOf(current)) ? File.ReadAllText(FileOf(current)) : null;
        }

        public string Current { get; private set; }

        public string Scene { get; set; }

        public string Loaded => memory;

        public string FailSelect { get; init; }

        public List<string> Selected { get; } = [];

        public List<string> Created { get; } = [];

        public IReadOnlyList<string> Collections(out string current)
        {
            current = Current;
            return collections.ToList();
        }

        public void Create(string name)
        {
            Save();
            collections.Add(name);
            Created.Add(name);
            Current = name;
            memory = $$"""{ "name": "{{name}}", "sources": [] }""";
        }

        public void Select(string name)
        {
            if (name == FailSelect)
            {
                throw new InvalidOperationException("OBS did not answer.");
            }

            Save();
            Selected.Add(name);
            Current = name;
            memory = File.ReadAllText(FileOf(name));
        }

        public string ProgramScene() => Scene;

        public void ShowScene(string name) => Scene = name;

        private void Save()
        {
            if (memory != null)
            {
                File.WriteAllText(FileOf(Current), memory);
            }
        }

        private string FileOf(string name) =>
            ObsLiveCollectionSwap.CollectionFile(scenes, name)
            ?? Path.Combine(scenes, name.Replace("-", "", StringComparison.Ordinal) + ".json");
    }
}
