using System;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

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
    bool IsStreamActive();
    void StartStream();
    void StopStream();
    event EventHandler<ObsRecordSignal> RecordSignal;
}

internal sealed class ObsWebsocketRecordSocket : IObsRecordSocket
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

    public void StartStream() => obs.StartStream();

    public void StopStream() => obs.StopStream();

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
