using HeroesReplay.Core.Obs.Inspection;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace HeroesReplay.Core.Obs.Recording;

/// <summary>
/// The short obs-websocket session <c>services stop</c> opens after every role exited: the
/// read-only Gets of <see cref="IObsReadSession"/>, plus <c>StopRecord</c>. Stopping a recording
/// is the only change it can make. It has no way to stop or start the stream (#318).
/// </summary>
public interface IObsRecordStopSession : IObsReadSession
{
    /// <returns>The path OBS gives for the stopped file, or null.</returns>
    /// <exception cref="ObsRequestException">OBS refused, for example no recording is active.</exception>
    string StopRecord();
}

public sealed class ObsWebsocketRecordStopSessionFactory
{
    /// <exception cref="ObsUnavailableException">OBS did not identify within a few seconds.</exception>
    public IObsRecordStopSession Open(string endpoint, string password) =>
        new ObsWebsocketRecordStopSession(
            ObsWebsocketReadSessionFactory.Connect(endpoint, password)
        );
}

internal sealed class ObsWebsocketRecordStopSession : IObsRecordStopSession
{
    private readonly OBSWebsocket obs;
    private readonly ObsWebsocketReadSession read;

    public ObsWebsocketRecordStopSession(OBSWebsocket obs)
    {
        this.obs = obs;
        read = new ObsWebsocketReadSession(obs);
    }

    public JObject Get(string requestType, JObject requestData = null) =>
        read.Get(requestType, requestData);

    public string StopRecord()
    {
        try
        {
            return obs.StopRecord();
        }
        catch (ErrorResponseException e)
        {
            throw new ObsRequestException("StopRecord", e.ErrorCode, e.Message);
        }
    }

    public void Dispose() => read.Dispose();
}
