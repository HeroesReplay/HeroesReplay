using System;
using HeroesReplay.Core.Obs.Inspection;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// What the supervisor does to a live OBS stream when spectate is down for good (its restart
/// budget is used up), so viewers do not watch a frozen game. Bound from
/// <c>ServiceRestart:SpectateDownObs</c>.
/// </summary>
public enum ObsFailSafeAction
{
    /// <summary>Show <c>OBS:WaitingSceneName</c>; the stream stays live.</summary>
    WaitingScene,

    /// <summary>Stop the stream.</summary>
    StopStream,

    /// <summary>Leave OBS as it is.</summary>
    None,
}

/// <summary>
/// The short obs-websocket session the supervisor opens to make OBS safe: the read-only Gets of
/// <see cref="IObsReadSession"/>, plus the two changes <see cref="ObsFailSafe"/> makes. It never
/// starts a stream or a recording.
/// </summary>
public interface IObsFailSafeSession : IObsReadSession
{
    /// <exception cref="ObsRequestException">OBS refused, for example a scene that does not exist.</exception>
    void ShowScene(string sceneName);

    /// <exception cref="ObsRequestException">OBS refused.</exception>
    void StopStream();
}

public sealed class ObsWebsocketFailSafeSessionFactory
{
    /// <exception cref="ObsUnavailableException">OBS did not identify within a few seconds.</exception>
    public IObsFailSafeSession Open(string endpoint, string password) =>
        new ObsWebsocketFailSafeSession(ObsWebsocketReadSessionFactory.Connect(endpoint, password));
}

internal sealed class ObsWebsocketFailSafeSession : IObsFailSafeSession
{
    private readonly OBSWebsocket obs;
    private readonly ObsWebsocketReadSession read;

    public ObsWebsocketFailSafeSession(OBSWebsocket obs)
    {
        this.obs = obs;
        read = new ObsWebsocketReadSession(obs);
    }

    public JObject Get(string requestType, JObject requestData = null) =>
        read.Get(requestType, requestData);

    public void ShowScene(string sceneName) =>
        Send("SetCurrentProgramScene", () => obs.SetCurrentProgramScene(sceneName));

    public void StopStream() => Send("StopStream", obs.StopStream);

    public void Dispose() => read.Dispose();

    private static void Send(string requestType, Action request)
    {
        try
        {
            request();
        }
        catch (ErrorResponseException e)
        {
            throw new ObsRequestException(requestType, e.ErrorCode, e.Message);
        }
    }
}

public static class ObsFailSafe
{
    /// <summary>
    /// Makes OBS safe after spectate went down for good and returns one line for the supervisor
    /// log. Only a stream this install may have started is touched: <c>OBS:StreamingEnabled</c>,
    /// this machine armed, OBS running, and the stream live. Never throws.
    /// </summary>
    public static string Apply(
        ObsFailSafeAction action,
        OBSSettings obs,
        bool armed,
        bool obsRunning,
        Func<IObsFailSafeSession> open
    )
    {
        if (action == ObsFailSafeAction.None)
        {
            return "OBS was left as it is (ServiceRestart:SpectateDownObs is None).";
        }

        if (!SessionMedia.ShouldStream(obs) || !armed)
        {
            return "OBS was left as it is: this install does not stream here (OBS:StreamingEnabled and the stream arm).";
        }

        if (!obsRunning)
        {
            return "OBS is not running, so there is no stream to make safe.";
        }

        try
        {
            using IObsFailSafeSession session = open();
            if (ObsResponse.Bool(session.Get("GetStreamStatus"), "outputActive") != true)
            {
                return "The OBS stream is not live, so OBS was left as it is.";
            }

            if (action == ObsFailSafeAction.StopStream)
            {
                session.StopStream();
                return "Stopped the OBS stream: nothing drives it while spectate is down.";
            }

            string scene = obs?.WaitingSceneName;
            if (string.IsNullOrWhiteSpace(scene))
            {
                return "OBS:WaitingSceneName is not set, so the OBS scene was not changed.";
            }

            session.ShowScene(scene);
            return "Switched OBS to the waiting scene '"
                + scene
                + "'. The stream stays live while spectate is down.";
        }
        catch (ObsUnavailableException e)
        {
            return "OBS was not reached, so the stream was not made safe. " + e.Message;
        }
        catch (ObsRequestException e)
        {
            return "OBS refused the change, so the stream was not made safe. " + e.Message;
        }
        catch (Exception e)
        {
            return "The stream was not made safe. " + e.Message;
        }
    }
}
