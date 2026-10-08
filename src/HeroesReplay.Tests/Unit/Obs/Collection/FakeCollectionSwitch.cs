using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs.Collection;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

/// <summary>
/// OBS as measured on ASA-SERVER: it lists the collections it started with or created, saves
/// the active one to its file when it switches away, and reads the next one from its file.
/// </summary>
internal sealed class FakeCollectionSwitch : IObsCollectionSwitch, IDisposable
{
    private readonly string scenes;
    private readonly List<string> collections;
    private string memory;

    public FakeCollectionSwitch(string scenes, string current, params string[] others)
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

    /// <summary>The swap session was closed.</summary>
    public bool Disposed { get; private set; }

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

    /// <summary>How many selects of <see cref="FailSelect"/> fail before OBS answers.</summary>
    public int FailSelectTimes { get; init; } = int.MaxValue;

    public int FailedSelects { get; private set; }

    public void Select(string name)
    {
        if (name == FailSelect && FailedSelects < FailSelectTimes)
        {
            FailedSelects++;
            throw new InvalidOperationException("OBS did not answer.");
        }

        Save();
        Selected.Add(name);
        Current = name;
        memory = File.ReadAllText(FileOf(name));
    }

    public string ProgramScene() => Scene;

    public void ShowScene(string name) => Scene = name;

    public void Dispose() => Disposed = true;

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
