using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Obs.Inspection;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary>
/// <see cref="IObsCollectionSwitch"/> over an identified OBS connection: the spectator's own, or
/// one short session of its own (<see cref="Open"/>) for <c>update restore-obs</c>.
/// </summary>
public sealed class ObsWebsocketCollectionSwitch : IObsCollectionSwitch, IDisposable
{
    private readonly OBSWebsocket obs;
    private readonly bool ownsConnection;

    internal ObsWebsocketCollectionSwitch(OBSWebsocket obs)
        : this(obs, ownsConnection: false) { }

    private ObsWebsocketCollectionSwitch(OBSWebsocket obs, bool ownsConnection)
    {
        this.obs = obs ?? throw new ArgumentNullException(nameof(obs));
        this.ownsConnection = ownsConnection;
    }

    /// <summary>
    /// OBS answers <c>SetCurrentSceneCollection</c> once the collection is loaded, which takes
    /// longer than a read.
    /// </summary>
    public static readonly TimeSpan SwitchTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Connects to OBS for one swap. Disposing it disconnects.</summary>
    /// <exception cref="ObsUnavailableException">OBS did not identify within a few seconds.</exception>
    public static ObsWebsocketCollectionSwitch Open(string endpoint, string password)
    {
        OBSWebsocket obs = ObsWebsocketReadSessionFactory.Connect(endpoint, password);
        obs.WSTimeout = SwitchTimeout;
        return new ObsWebsocketCollectionSwitch(obs, ownsConnection: true);
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

    /// <summary>Disconnects a session this opened. The spectator's connection is left open.</summary>
    public void Dispose()
    {
        if (ownsConnection)
        {
            ObsWebsocketReadSessionFactory.Close(obs);
        }
    }
}
