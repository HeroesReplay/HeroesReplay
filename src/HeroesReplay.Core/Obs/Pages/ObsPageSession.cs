using HeroesReplay.Core.Obs.Inspection;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace HeroesReplay.Core.Obs.Pages;

/// <summary>
/// The short obs-websocket session <c>obs pages</c> opens: the read-only Get requests of
/// <see cref="IObsReadSession"/>, plus a reload of one browser source. Reloading a page is the
/// only change it can make in OBS. It does not select a scene or change a source's settings.
/// </summary>
public interface IObsPageSession : IObsReadSession
{
    /// <summary>Reloads the browser source <paramref name="inputName"/> without its cache.</summary>
    /// <exception cref="ObsRequestException">OBS refused the reload, for example a missing source.</exception>
    void Reload(string inputName);
}

public sealed class ObsWebsocketPageSessionFactory
{
    /// <exception cref="ObsUnavailableException">OBS did not identify within a few seconds.</exception>
    public IObsPageSession Open(string endpoint, string password) =>
        new ObsWebsocketPageSession(ObsWebsocketReadSessionFactory.Connect(endpoint, password));
}

internal sealed class ObsWebsocketPageSession : IObsPageSession
{
    /// <summary>The obs-browser property button behind "Refresh cache of current page".</summary>
    public const string RefreshButton = "refreshnocache";

    private readonly OBSWebsocket obs;
    private readonly ObsWebsocketReadSession read;

    public ObsWebsocketPageSession(OBSWebsocket obs)
    {
        this.obs = obs;
        read = new ObsWebsocketReadSession(obs);
    }

    public JObject Get(string requestType, JObject requestData = null) =>
        read.Get(requestType, requestData);

    public void Reload(string inputName)
    {
        try
        {
            obs.PressInputPropertiesButton(inputName, RefreshButton);
        }
        catch (ErrorResponseException e)
        {
            throw new ObsRequestException("PressInputPropertiesButton", e.ErrorCode, e.Message);
        }
    }

    public void Dispose() => read.Dispose();
}
