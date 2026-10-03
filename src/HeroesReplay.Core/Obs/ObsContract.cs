using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Obs;

/// <summary>A source HeroesReplay shows, hides, or updates inside a named scene.</summary>
public sealed record ObsContractItem(string Scene, string Source);

/// <summary>
/// The scenes and sources HeroesReplay drives by name: <c>OBS:GameSceneName</c>,
/// <c>WaitingSceneName</c>, the info and rank sources inside the game scene, and each enabled
/// report scene with its browser source. <c>check obs</c> looks for them in the packaged
/// collection and <c>obs_validate</c> in the one OBS has loaded.
/// </summary>
public sealed record ObsContract(
    IReadOnlyList<string> Scenes,
    IReadOnlyList<string> Sources,
    IReadOnlyList<ObsContractItem> Items
)
{
    /// <summary>
    /// The base (canvas) resolution the collection is laid out for: the scene items, the
    /// full-canvas report browser sources, and the match report's one-canvas scroll. The output
    /// (scaled) resolution is the machine's choice.
    /// </summary>
    public const int CanvasWidth = 1920;

    public const int CanvasHeight = 1080;

    /// <summary>Below this the stream and recordings stutter on the game's motion.</summary>
    public const double MinimumFps = 30;

    /// <summary>The named service (<c>rtmp_common</c>) the stream goes to.</summary>
    public const string StreamService = "Twitch";

    public static ObsContract From(OBSSettings obs)
    {
        var scenes = new List<string>();
        var sources = new List<string>();
        var items = new List<ObsContractItem>();
        if (obs == null)
        {
            return new ObsContract(scenes, sources, items);
        }

        Add(scenes, obs.GameSceneName);
        Add(scenes, obs.WaitingSceneName);

        // ObsController toggles these with GetSceneItemId on the game scene.
        var inGame = new List<string>();
        Add(inGame, obs.InfoSourceName);
        Add(inGame, obs.TierDivisionSourceName);
        Add(inGame, obs.TierRankPointsSourceName);
        foreach (string name in obs.RankImagesSourceNames ?? RankImage.SourceNames)
        {
            Add(inGame, name);
        }

        sources.AddRange(inGame);
        if (!string.IsNullOrWhiteSpace(obs.GameSceneName))
        {
            items.AddRange(inGame.Select(source => new ObsContractItem(obs.GameSceneName, source)));
        }

        foreach (ReportScene scene in obs.ReportScenes ?? [])
        {
            if (scene == null || !scene.Enabled)
            {
                continue;
            }

            Add(scenes, scene.SceneName);
            Add(sources, scene.SourceName);
            if (
                !string.IsNullOrWhiteSpace(scene.SceneName)
                && !string.IsNullOrWhiteSpace(scene.SourceName)
            )
            {
                items.Add(new ObsContractItem(scene.SceneName, scene.SourceName));
            }
        }

        return new ObsContract(
            scenes.Distinct(StringComparer.Ordinal).ToList(),
            sources.Distinct(StringComparer.Ordinal).ToList(),
            items.Distinct().ToList()
        );
    }

    private static void Add(List<string> names, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            names.Add(value);
        }
    }
}
