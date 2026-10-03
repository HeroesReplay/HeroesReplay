using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary><see cref="IObsCollectionSwitch"/> over the spectator's identified OBS connection.</summary>
internal sealed class ObsWebsocketCollectionSwitch : IObsCollectionSwitch
{
    private readonly OBSWebsocket obs;

    public ObsWebsocketCollectionSwitch(OBSWebsocket obs)
    {
        this.obs = obs ?? throw new ArgumentNullException(nameof(obs));
    }

    public IReadOnlyList<string> Collections(out string current)
    {
        JObject response = obs.SendRequest("GetSceneCollectionList") ?? new JObject();
        current = (string)response["currentSceneCollectionName"];
        return (response["sceneCollections"] as JArray ?? [])
            .Select(name => (string)name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
    }

    public void Create(string name) =>
        obs.SendRequest("CreateSceneCollection", new JObject { ["sceneCollectionName"] = name });

    public void Select(string name) =>
        obs.SendRequest(
            "SetCurrentSceneCollection",
            new JObject { ["sceneCollectionName"] = name }
        );

    public string ProgramScene()
    {
        JObject response = obs.SendRequest("GetCurrentProgramScene") ?? new JObject();
        return (string)(response["sceneName"] ?? response["currentProgramSceneName"]);
    }

    public void ShowScene(string name) =>
        obs.SendRequest("SetCurrentProgramScene", new JObject { ["sceneName"] = name });
}
