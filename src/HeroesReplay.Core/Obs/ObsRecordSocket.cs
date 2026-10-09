using System;
using System.Threading;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;

namespace HeroesReplay.Core.Obs;

internal enum ObsRecordSignalKind
{
    Started,
    Stopped,
    Split,
    Disconnected,
}

internal sealed class ObsRecordSignal : EventArgs
{
    public ObsRecordSignalKind Kind { get; }
    public string OutputPath { get; }

    private ObsRecordSignal(ObsRecordSignalKind kind, string outputPath)
    {
        Kind = kind;
        OutputPath = outputPath;
    }

    public static ObsRecordSignal Started() => new(ObsRecordSignalKind.Started, null);

    public static ObsRecordSignal Stopped(string path) => new(ObsRecordSignalKind.Stopped, path);

    public static ObsRecordSignal Split(string path) => new(ObsRecordSignalKind.Split, path);

    public static ObsRecordSignal Disconnected() => new(ObsRecordSignalKind.Disconnected, null);
}

internal interface IObsRecordSocket
{
    bool IsIdentified { get; }
    bool IsConnected { get; }
    bool IsRecording();
    void StartRecord();
    string StopRecord();

    /// <summary>GetStreamStatus <c>outputActive</c>: the output runs, live or not (#395).</summary>
    bool IsStreamActive();

    /// <summary>GetStreamStatus: active, reconnecting, and bytes sent, for <see cref="ObsStreamHealth"/>.</summary>
    ObsStreamSample ReadStream();
    void StartStream();
    void StopStream();
    event EventHandler<ObsRecordSignal> RecordSignal;
}

internal interface IObsSession : IObsRecordSocket
{
    void Connect(string endpoint, string password, TimeSpan identifyTimeout);
    void Disconnect();
    void SelectProgramScene(string sceneName);
    string ProgramScene { get; }

    /// <summary>GetProfileList → currentProfileName.</summary>
    string CurrentProfile();

    /// <summary>GetSceneCollectionList → currentSceneCollectionName.</summary>
    string CurrentSceneCollection();
}

internal sealed class ObsWebsocketRecordSocket : IObsSession
{
    private readonly OBSWebsocket obs;

    public ObsWebsocketRecordSocket(OBSWebsocket obs)
    {
        this.obs = obs ?? throw new ArgumentNullException(nameof(obs));
        obs.RecordStateChanged += OnRecordStateChanged;
        obs.RecordFileChanged += OnRecordFileChanged;
        obs.Disconnected += OnDisconnected;
    }

    public event EventHandler<ObsRecordSignal> RecordSignal;

    public bool IsIdentified => obs.IsIdentified;

    public bool IsConnected => obs.IsConnected;

    public bool IsRecording()
    {
        RecordingStatus status = obs.GetRecordStatus();
        return status != null && status.IsRecording;
    }

    public void StartRecord() => obs.StartRecord();

    public string StopRecord() => obs.StopRecord();

    public bool IsStreamActive()
    {
        OutputStatus status = obs.GetStreamStatus();
        return status != null && status.IsActive;
    }

    public ObsStreamSample ReadStream()
    {
        OutputStatus status =
            obs.GetStreamStatus()
            ?? throw new InvalidOperationException("GetStreamStatus returned no status.");
        return new ObsStreamSample(status.IsActive, status.IsReconnecting, status.BytesSent);
    }

    public void StartStream() => obs.StartStream();

    public void StopStream() => obs.StopStream();

    public string ProgramScene => obs.GetCurrentProgramScene();

    public void SelectProgramScene(string sceneName) => obs.SetCurrentProgramScene(sceneName);

    public string CurrentProfile() => obs.GetProfileList()?.CurrentProfileName;

    public string CurrentSceneCollection() => obs.GetCurrentSceneCollection();

    public void Connect(string endpoint, string password, TimeSpan identifyTimeout)
    {
        if (obs.IsIdentified)
        {
            return;
        }

        using var identified = new ManualResetEventSlim(false);
        EventHandler handler = (_, _) => identified.Set();
        obs.Connected += handler;
        try
        {
            if (!obs.IsConnected)
            {
                obs.ConnectAsync(endpoint, password ?? string.Empty);
            }

            if (obs.IsIdentified)
            {
                return;
            }

            if (!identified.Wait(identifyTimeout))
            {
                throw new TimeoutException(
                    "OBS websocket at "
                        + endpoint
                        + " did not identify in time. "
                        + "OBS Studio 30.0+ (obs-websocket 5.3+) is required, on port 4455 (Tools > WebSocket Server Settings)."
                );
            }
        }
        finally
        {
            obs.Connected -= handler;
        }
    }

    public void Disconnect()
    {
        if (obs.IsConnected)
        {
            obs.Disconnect();
        }
    }

    private void OnRecordStateChanged(object sender, RecordStateChangedEventArgs args)
    {
        RecordStateChanged state = args?.OutputState;
        if (state == null)
        {
            return;
        }

        OutputState output;
        try
        {
            output = state.State;
        }
        catch (ArgumentException)
        {
            return;
        }

        if (output == OutputState.OBS_WEBSOCKET_OUTPUT_STOPPED)
        {
            Raise(ObsRecordSignal.Stopped(state.OutputPath));
            return;
        }

        if (output == OutputState.OBS_WEBSOCKET_OUTPUT_STARTED || state.IsActive)
        {
            Raise(ObsRecordSignal.Started());
        }
    }

    private void OnRecordFileChanged(object sender, RecordFileChangedEventArgs args) =>
        Raise(ObsRecordSignal.Split(args?.NewOutputPath));

    private void OnDisconnected(object sender, ObsDisconnectionInfo info) =>
        Raise(ObsRecordSignal.Disconnected());

    private void Raise(ObsRecordSignal signal) => RecordSignal?.Invoke(this, signal);
}
