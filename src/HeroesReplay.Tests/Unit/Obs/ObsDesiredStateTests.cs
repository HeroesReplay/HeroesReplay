using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.Core.Services.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsDesiredStateTests
{
    private const string Endpoint = "ws://127.0.0.1:9";
    private const string WaitingScene = "waiting-screen";

    [Fact]
    public void Reconcile_AbsentObs_ReportsLaunchAndDoesNotStartARealProcess()
    {
        string executable = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-absent-obs-" + Guid.NewGuid().ToString("N"),
            "obs64.exe"
        );
        var socket = new FakeSession();
        var process = new FakeProcess { Exists = true };
        Harness harness = Open(
            TwitchIngestGuard.ProductionHost,
            Settings(executable),
            socket,
            process
        );
        int obsBefore = CountObs64();
        var clock = Stopwatch.StartNew();

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        clock.Stop();
        Assert.Equal(obsBefore, CountObs64());
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), clock.Elapsed.ToString());
        Assert.Empty(harness.Waits);
        Assert.False(File.Exists(executable));
        Assert.Equal(1, process.LaunchCalls);
        Assert.Equal(ObsLaunchKind.Launch, snapshot.Launch.Kind);
        Assert.False(snapshot.Launch.Started);
        Assert.Equal(executable, snapshot.Launch.ExecutablePath);
        Assert.DoesNotContain(
            "Program Files",
            snapshot.Launch.ExecutablePath,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Equal(ObsLaunchDecision.ArgumentsForHeroesReplay(), snapshot.Launch.Arguments);
        Assert.Contains("--profile \"HeroesReplay\"", snapshot.Launch.Arguments);
        Assert.Contains("--collection \"HeroesReplay\"", snapshot.Launch.Arguments);
        Assert.DoesNotContain(
            "startstreaming",
            snapshot.Launch.Arguments,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Equal(Endpoint, socket.LastEndpoint);
        Assert.DoesNotContain("4455", socket.LastEndpoint);
        Assert.Equal("unit-password", socket.LastPassword);
        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(0, socket.SelectCalls);
        Assert.False(snapshot.Stream.Succeeded);
        Assert.False(snapshot.Stream.Active);
        Assert.Equal(ObsOutputFailure.NotConfirmed, snapshot.Stream.Failure);
        Assert.Equal("OBS websocket did not identify.", snapshot.Stream.Detail);
        Assert.True(snapshot.ProcessDesired);
        Assert.True(snapshot.WebsocketDesired);
        Assert.False(snapshot.WebsocketIdentified);
        Assert.True(snapshot.StreamDesired);
        Assert.False(snapshot.StreamActive);
        Assert.Equal(WaitingScene, snapshot.SceneDesired);
    }

    [Fact]
    public void Reconcile_RepairsStoppedStreamWithoutAnInternetTransition()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
            ProgramScene = "game-scene",
        };
        Harness harness = Open(TwitchIngestGuard.ProductionHost, Settings(), socket);

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.Equal(new[] { "scene:" + WaitingScene, "start" }, socket.Events);
        Assert.Equal(1, socket.StartStreamCalls);
        Assert.Equal(1, socket.SelectCalls);
        Assert.True(snapshot.Stream.Succeeded);
        Assert.True(snapshot.Stream.Active);
        Assert.Equal(ObsOutputFailure.None, snapshot.Stream.Failure);
        Assert.Equal("OBS reported the stream active.", snapshot.Stream.Detail);
        Assert.True(snapshot.StreamDesired);
        Assert.True(snapshot.StreamActive);
        Assert.Equal(WaitingScene, snapshot.SceneDesired);
        Assert.Equal(WaitingScene, snapshot.SceneActual);
        Assert.Equal(0, harness.Process.LaunchCalls);

        ObsRuntimeSnapshot again = harness.Coordinator.ReconcileStream();

        Assert.Equal(1, socket.StartStreamCalls);
        Assert.Equal(1, socket.SelectCalls);
        Assert.True(again.Stream.Succeeded);
        Assert.True(again.Stream.Active);
        Assert.Equal(WaitingScene, again.SceneActual);
    }

    [Fact]
    public void Reconcile_ActiveStream_DoesNotChangeTheProgramScene()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            Streaming = true,
            ProgramScene = "game-scene",
        };
        Harness harness = Open(TwitchIngestGuard.ProductionHost, Settings(), socket);

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.Empty(socket.Events);
        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(0, socket.SelectCalls);
        Assert.True(snapshot.Stream.Succeeded);
        Assert.True(snapshot.Stream.Active);
        Assert.Equal("game-scene", snapshot.SceneActual);
        Assert.Equal(WaitingScene, snapshot.SceneDesired);
    }

    [Fact]
    public void Reconcile_StreamingDisabled_DoesNotStart()
    {
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        int patches = 0;
        Harness harness = Open(
            TwitchIngestGuard.ProductionHost,
            Settings(streaming: false),
            socket,
            beforeLaunch: () => patches++
        );

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.Equal(0, patches);
        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(0, socket.StopStreamCalls);
        Assert.Equal(0, socket.SelectCalls);
        Assert.Equal(0, socket.ConnectCalls);
        Assert.Equal(0, harness.Process.LaunchCalls);
        Assert.False(snapshot.StreamDesired);
        Assert.Null(snapshot.SceneDesired);
        Assert.False(snapshot.Stream.Succeeded);
        Assert.Equal(ObsOutputFailure.NotRequested, snapshot.Stream.Failure);
        Assert.Equal("OBS streaming is disabled.", snapshot.Stream.Detail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ASA-SERVER")]
    [InlineData("asa-server")]
    [InlineData("RANDOM-PC")]
    public void Reconcile_NonProductionHost_DoesNotStart(string host)
    {
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        int patches = 0;
        Harness harness = Open(host, Settings(), socket, beforeLaunch: () => patches++);

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.Equal(0, patches);
        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(0, socket.SelectCalls);
        Assert.Equal(0, socket.ConnectCalls);
        Assert.Equal(0, harness.Process.LaunchCalls);
        Assert.False(snapshot.StreamDesired);
        Assert.False(snapshot.Stream.Succeeded);
        Assert.Equal(ObsOutputFailure.NotRequested, snapshot.Stream.Failure);
        Assert.Equal(TwitchIngestGuard.NotStartedMessage, snapshot.Stream.Detail);
    }

    [Fact]
    public void Reconcile_UnreadableMachineName_DoesNotStart()
    {
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        Harness harness = Open(
            TwitchIngestGuard.ProductionHost,
            Settings(),
            socket,
            machineName: () => throw new InvalidOperationException("unnamed")
        );

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(0, harness.Process.LaunchCalls);
        Assert.Equal(ObsOutputFailure.NotRequested, snapshot.Stream.Failure);
        Assert.Equal(TwitchIngestGuard.NotStartedMessage, snapshot.Stream.Detail);
    }

    [Fact]
    public void Reconcile_ObsDisabled_DoesNotStart()
    {
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        Harness harness = Open(TwitchIngestGuard.ProductionHost, Settings(enabled: false), socket);

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(0, harness.Process.LaunchCalls);
        Assert.False(snapshot.ProcessDesired);
        Assert.False(snapshot.StreamDesired);
        Assert.Equal(ObsOutputFailure.NotRequested, snapshot.Stream.Failure);
    }

    [Fact]
    public void Reconcile_BlankWaitingScene_DoesNotStart()
    {
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        Harness harness = Open(TwitchIngestGuard.ProductionHost, Settings(scene: " "), socket);

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(0, socket.SelectCalls);
        Assert.Equal(ObsOutputFailure.NotConfirmed, snapshot.Stream.Failure);
        Assert.Equal("Waiting scene is not configured.", snapshot.Stream.Detail);
        Assert.False(snapshot.Stream.Succeeded);
    }

    [Fact]
    public void Reconcile_SceneSelectionFailure_DoesNotStart()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            SelectError = new InvalidOperationException("scene missing"),
        };
        Harness harness = Open(TwitchIngestGuard.ProductionHost, Settings(), socket);

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(1, socket.SelectCalls);
        Assert.False(snapshot.Stream.Succeeded);
        Assert.Equal(ObsOutputFailure.NotConfirmed, snapshot.Stream.Failure);
        Assert.Equal("scene missing", snapshot.Stream.Detail);
    }

    [Fact]
    public void Reconcile_AlreadyRunning_DoesNotLaunch()
    {
        var socket = new FakeSession { IdentifyOnConnect = true, ActivateOnStart = true };
        var process = new FakeProcess { Exists = true, Running = true };
        Harness harness = Open(TwitchIngestGuard.ProductionHost, Settings(), socket, process);

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.Equal(ObsLaunchKind.AlreadyRunning, snapshot.Launch.Kind);
        Assert.False(snapshot.Launch.Started);
        Assert.False(snapshot.ProcessOwned);
        Assert.Equal(0, process.LaunchCalls);
        Assert.Equal(0, process.CloseCalls);
        Assert.Equal(1, socket.StartStreamCalls);
        Assert.True(snapshot.Stream.Succeeded);
        Assert.True(snapshot.Stream.Active);
    }

    [Fact]
    public void Reconcile_BackoffWaitsWithoutSleeping()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnCall = 3,
        };
        var backoff = new ObsBackoff(3, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        Harness harness = Open(
            TwitchIngestGuard.ProductionHost,
            Settings(),
            socket,
            backoff: backoff,
            budget: Fast(0)
        );
        var clock = Stopwatch.StartNew();

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), clock.Elapsed.ToString());
        Assert.Equal(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5) }, harness.Waits);
        Assert.Equal(3, socket.StartStreamCalls);
        Assert.True(snapshot.Stream.Succeeded);
        Assert.True(snapshot.Stream.Active);
        Assert.Equal(1, socket.SelectCalls);
    }

    [Fact]
    public void Shutdown_ConfirmsInactive()
    {
        Harness harness = Streaming();

        ObsShutdownResult result = harness.Coordinator.Shutdown();

        Assert.True(result.Stream.Succeeded);
        Assert.False(result.Stream.Active);
        Assert.Equal(ObsOutputFailure.None, result.Stream.Failure);
        Assert.Equal("OBS reported the stream inactive.", result.Stream.Detail);
        Assert.Equal(1, harness.Socket.StopStreamCalls);
        Assert.False(harness.Socket.Streaming);
        Assert.False(harness.Coordinator.State.StreamActive);
        Assert.True(harness.Coordinator.State.StreamDesired);
        Assert.Equal(0, harness.Process.LaunchCalls);
        Assert.Equal(0, harness.Process.CloseCalls);
    }

    [Fact]
    public void Shutdown_TimesOutWithoutSuccess()
    {
        Harness harness = Streaming();
        harness.Socket.KeepStreamingOnStop = true;

        ObsShutdownResult result = harness.Coordinator.Shutdown();

        Assert.False(result.Stream.Succeeded);
        Assert.False(result.Stream.Active);
        Assert.Equal(ObsOutputFailure.Timeout, result.Stream.Failure);
        Assert.Equal(
            "Timed out waiting for OBS to report the stream inactive.",
            result.Stream.Detail
        );
        Assert.NotEqual(ObsStreamResult.Success().Detail, result.Stream.Detail);
        Assert.Equal(1, harness.Socket.StopStreamCalls);
        Assert.True(harness.Socket.Streaming);
        Assert.True(harness.Coordinator.State.StreamDesired);
        Assert.True(harness.Coordinator.State.StreamActive);
        Assert.Equal(0, harness.Process.LaunchCalls);
    }

    [Fact]
    public void Shutdown_Unidentified_IsNotConfirmedAndDoesNotLaunch()
    {
        var socket = new FakeSession();
        var process = new FakeProcess { Exists = true };
        Harness harness = Open(TwitchIngestGuard.ProductionHost, Settings(), socket, process);

        ObsShutdownResult result = harness.Coordinator.Shutdown();

        Assert.False(result.Stream.Succeeded);
        Assert.Equal(ObsOutputFailure.NotConfirmed, result.Stream.Failure);
        Assert.Equal(0, socket.StopStreamCalls);
        Assert.Equal(0, process.LaunchCalls);
        Assert.Equal(1, socket.ConnectCalls);
        Assert.Equal(Endpoint, socket.LastEndpoint);
        Assert.DoesNotContain("4455", socket.LastEndpoint);
    }

    [Fact]
    public void Shutdown_StreamingDisabled_DoesNotStop()
    {
        Harness harness = Streaming(streaming: false);

        ObsShutdownResult result = harness.Coordinator.Shutdown();

        Assert.Equal(ObsOutputFailure.NotRequested, result.Stream.Failure);
        Assert.Equal("OBS streaming is disabled.", result.Stream.Detail);
        Assert.Equal(0, harness.Socket.StopStreamCalls);
        Assert.Equal(0, harness.Process.LaunchCalls);
        Assert.True(harness.Socket.Streaming);
    }

    [Fact]
    public void Shutdown_UnownedProcessIsNotClosed()
    {
        Harness harness = Streaming(closeOwned: true);
        harness.Process.Owned = false;
        harness.Process.Running = true;

        ObsShutdownResult result = harness.Coordinator.Shutdown();

        Assert.False(result.Plan.CloseProcess);
        Assert.True(result.Plan.StopStream);
        Assert.Contains("did not start", result.Plan.Detail, StringComparison.Ordinal);
        Assert.Equal(0, harness.Process.CloseCalls);
        Assert.True(result.Stream.Succeeded);
        Assert.False(result.Stream.Active);
    }

    [Fact]
    public void Shutdown_OwnedProcessClosesWhenRequested()
    {
        Harness harness = Streaming(closeOwned: true);
        harness.Process.Owned = true;

        ObsShutdownResult result = harness.Coordinator.Shutdown();

        Assert.True(result.Plan.CloseProcess);
        Assert.Equal(1, harness.Process.CloseCalls);
        Assert.True(result.Stream.Succeeded);
        Assert.False(result.Stream.Active);
    }

    [Fact]
    public void Shutdown_OwnedProcessStaysUpWhenCloseFlagIsOff()
    {
        Harness harness = Streaming(closeOwned: false);
        harness.Process.Owned = true;

        ObsShutdownResult result = harness.Coordinator.Shutdown();

        Assert.False(result.Plan.CloseProcess);
        Assert.Equal(0, harness.Process.CloseCalls);
        Assert.Contains("Close-on-stop is off", result.Plan.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void StartStream_ExceptionLeavesTheRetryDelegate()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            StartStreamError = new InvalidOperationException("start failed"),
        };
        var session = new RecordingSession(NullLogger.Instance, socket, Fast(1));

        ObsStreamResult result = session.StartStreaming(() => { });

        Assert.Equal(2, socket.StartStreamCalls);
        Assert.False(result.Succeeded);
        Assert.False(result.Active);
        Assert.Equal(ObsOutputFailure.RequestError, result.Failure);
        Assert.Equal("start failed", result.Detail);
    }

    [Fact]
    public void StopStream_AlreadyInactive_IsConfirmedFromTheStatusRead()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            Streaming = false,
        };
        var session = new RecordingSession(NullLogger.Instance, socket, Fast(0));

        ObsStreamResult result = session.StopStreaming(() => { });

        Assert.True(result.Succeeded);
        Assert.False(result.Active);
        Assert.Equal(ObsOutputFailure.None, result.Failure);
        Assert.Equal(0, socket.StopStreamCalls);
    }

    [Fact]
    public void StartRecording_DoesNotAdoptAnExistingRecording()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            Recording = true,
        };
        Harness harness = Open(TwitchIngestGuard.DevelopmentHost, Settings(), socket);

        ObsRecordingResult started = harness.Coordinator.StartRecording(() => true, 7, "unit");
        ObsRecordingResult stopped = harness.Coordinator.StopRecording(7);

        Assert.False(started.Succeeded);
        Assert.False(started.Owned);
        Assert.Equal(ObsOutputFailure.AlreadyRecording, started.Failure);
        Assert.Equal(0, socket.StartRecordCalls);
        Assert.Equal(ObsOutputFailure.NotOwned, stopped.Failure);
        Assert.Null(stopped.OutputPath);
        Assert.Equal(0, socket.StopRecordCalls);
        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(0, harness.Process.LaunchCalls);
    }

    [Fact]
    public void Backoff_DelaysArePureAndInjected()
    {
        Assert.Equal(
            new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) },
            ObsBackoff.Delays(4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8))
        );
        Assert.Equal(
            new[]
            {
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(3),
            },
            ObsBackoff.Delays(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3))
        );
        Assert.Empty(ObsBackoff.Delays(1, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8)));

        var waits = new List<TimeSpan>();
        int calls = 0;
        var clock = Stopwatch.StartNew();
        ObsStreamResult result = ObsBackoff.Run(
            new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30) },
            () =>
            {
                calls++;
                return calls == 3
                    ? ObsStreamResult.ConfirmedActive()
                    : ObsStreamResult.Failed(ObsOutputFailure.NotConfirmed, "not yet");
            },
            waits.Add
        );
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), clock.Elapsed.ToString());
        Assert.Equal(3, calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30) }, waits);
        Assert.True(result.Succeeded);
        Assert.True(result.Active);

        waits.Clear();
        ObsStreamResult refused = ObsBackoff.Run(
            new[] { TimeSpan.FromSeconds(30) },
            () => ObsStreamResult.NotRequested("off"),
            waits.Add
        );
        Assert.Empty(waits);
        Assert.Equal(ObsOutputFailure.NotRequested, refused.Failure);

        waits.Clear();
        ObsBackoff.Run(
            new[] { TimeSpan.FromSeconds(30) },
            () => ObsStreamResult.ConfirmedInactive(),
            waits.Add
        );
        Assert.Empty(waits);
    }

    [Fact]
    public void LaunchDecision_UsesProfileAndCollectionWithoutStartStreaming()
    {
        string path = Path.Combine(Path.GetTempPath(), "missing-obs64.exe");
        ObsLaunchDecision launch = ObsLaunchDecision.Decide(true, false, path, true);
        Assert.Equal(ObsLaunchKind.Launch, launch.Kind);
        Assert.False(launch.Started);
        Assert.Equal(path, launch.ExecutablePath);
        Assert.Equal("--profile \"HeroesReplay\" --collection \"HeroesReplay\"", launch.Arguments);
        Assert.DoesNotContain(
            "startstreaming",
            launch.Arguments,
            StringComparison.OrdinalIgnoreCase
        );

        ObsLaunchDecision running = ObsLaunchDecision.Decide(true, true, path, true);
        Assert.Equal(ObsLaunchKind.AlreadyRunning, running.Kind);
        Assert.Null(running.Arguments);
        Assert.False(running.Started);

        ObsLaunchDecision missing = ObsLaunchDecision.Decide(true, false, path, false);
        Assert.Equal(ObsLaunchKind.Missing, missing.Kind);
        Assert.False(missing.Started);

        ObsLaunchDecision skipped = ObsLaunchDecision.Decide(false, false, path, true);
        Assert.Equal(ObsLaunchKind.Skipped, skipped.Kind);
        Assert.Equal("OBS is disabled.", skipped.Detail);

        Assert.Equal(path, ObsLaunchDecision.ResolveExecutable(path));
        Assert.EndsWith(
            Path.Combine("obs-studio", "bin", "64bit", "obs64.exe"),
            ObsLaunchDecision.ResolveExecutable(null)
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ASA-SERVER")]
    [InlineData("asa-server")]
    [InlineData("RANDOM-PC")]
    public void Guard_RefusesNonProductionHosts(string host)
    {
        Assert.False(TwitchIngestGuard.IsProductionHost(host));
        Assert.False(TwitchIngestGuard.Allows(host, true));
        Assert.False(TwitchIngestGuard.Allows(host, false));
        Assert.Equal(TwitchIngestGuard.NotStartedMessage, TwitchIngestGuard.Refusal(host, true));
        Assert.Equal("OBS streaming is disabled.", TwitchIngestGuard.Refusal(host, false));
    }

    [Theory]
    [InlineData("DESKTOP-8SJE72")]
    [InlineData("desktop-8sje72")]
    [InlineData("  DESKTOP-8SJE72 ")]
    public void Guard_AllowsOnlyTheProductionHostWhenStreamingIsEnabled(string host)
    {
        Assert.True(TwitchIngestGuard.IsProductionHost(host));
        Assert.True(TwitchIngestGuard.Allows(host, true));
        Assert.False(TwitchIngestGuard.Allows(host, false));
        Assert.Equal("OBS streaming is disabled.", TwitchIngestGuard.Refusal(host, false));
    }

    [Fact]
    public void Startup_DesiredStateRequiresEnabledStreamingAndProductionHost()
    {
        OBSSettings obs = Settings();
        Assert.True(ObsDesired.StreamIsDesired(obs, TwitchIngestGuard.ProductionHost));
        Assert.False(ObsDesired.StreamIsDesired(obs, TwitchIngestGuard.DevelopmentHost));
        Assert.False(
            ObsDesired.StreamIsDesired(Settings(streaming: false), TwitchIngestGuard.ProductionHost)
        );
        Assert.False(
            ObsDesired.StreamIsDesired(Settings(enabled: false), TwitchIngestGuard.ProductionHost)
        );

        ObsRuntimeSnapshot snapshot = ObsDesired.Capture(
            obs,
            TwitchIngestGuard.ProductionHost,
            processRunning: false,
            processOwned: false,
            launch: ObsLaunchDecision.Decide(true, false, obs.ExecutablePath, true),
            identified: false,
            sceneActual: null,
            streamActive: false,
            recordingDesired: false,
            recordingActive: false,
            stream: null
        );

        Assert.True(snapshot.ProcessDesired);
        Assert.False(snapshot.ProcessRunning);
        Assert.True(snapshot.WebsocketDesired);
        Assert.False(snapshot.WebsocketIdentified);
        Assert.Equal(WaitingScene, snapshot.SceneDesired);
        Assert.Null(snapshot.SceneActual);
        Assert.True(snapshot.StreamDesired);
        Assert.False(snapshot.StreamActive);
        Assert.Equal(ObsLaunchKind.Launch, snapshot.Launch.Kind);
    }

    [Fact]
    public void ObsStatus_CopiesDesiredVersusActualWithoutWritingStatusJson()
    {
        string live = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "status.json"
        );
        DateTime? before = File.Exists(live) ? File.GetLastWriteTimeUtc(live) : null;
        var status = new SpectatorStatus();
        ObsStatus.Copy(status, null);
        Assert.Null(status.ObsStreamDesired);
        Assert.Null(ObsStatus.Describe(status));

        var snapshot = new ObsRuntimeSnapshot
        {
            ProcessRunning = false,
            WebsocketIdentified = false,
            SceneDesired = WaitingScene,
            SceneActual = null,
            StreamDesired = true,
            StreamActive = false,
            Stream = ObsStreamResult.Failed(
                ObsOutputFailure.NotConfirmed,
                "OBS websocket did not identify."
            ),
        };
        ObsStatus.Copy(status, snapshot);

        Assert.Equal(false, status.ObsProcessRunning);
        Assert.Equal(false, status.ObsWebsocketIdentified);
        Assert.Equal(WaitingScene, status.ObsSceneDesired);
        Assert.Null(status.ObsSceneActual);
        Assert.Equal(true, status.ObsStreamDesired);
        Assert.Equal(false, status.ObsStreamActive);
        Assert.Equal("OBS websocket did not identify.", status.ObsDetail);
        string line = ObsStatus.Describe(status);
        Assert.Contains("process=False", line, StringComparison.Ordinal);
        Assert.Contains("websocket=False", line, StringComparison.Ordinal);
        Assert.Contains("scene=" + WaitingScene, line, StringComparison.Ordinal);
        Assert.Contains("stream desired=True", line, StringComparison.Ordinal);
        Assert.Contains("active=False", line, StringComparison.Ordinal);
        DateTime? after = File.Exists(live) ? File.GetLastWriteTimeUtc(live) : null;
        Assert.Equal(before, after);
    }

    [Fact]
    public void ServicesStop_RequestsStopAndDoesNotCloseUnownedObs()
    {
        ObsShutdownPlan servicesPlan = ObsServiceStop.PlanForServicesCommand();
        Assert.True(servicesPlan.StopStream);
        Assert.False(servicesPlan.CloseProcess);
        ObsStreamResult delegated = ObsServiceStop.DelegateToSpectator(servicesPlan);
        Assert.Equal(ObsOutputFailure.NotRequested, delegated.Failure);
        Assert.Contains("does not open a websocket", delegated.Detail, StringComparison.Ordinal);
        ObsStreamResult refused = ObsServiceStop.DelegateToSpectator(
            ObsShutdownPlan.For(ownsProcess: true, closeOwnedProcess: true, streamMayBeActive: true)
        );
        Assert.Equal(ObsOutputFailure.NotOwned, refused.Failure);

        string path = Path.Combine(
            Path.GetTempPath(),
            $"heroesreplay-services-{Guid.NewGuid():N}.json"
        );
        try
        {
            ObsShutdownPlan seen = null;
            int kills = 0;
            int code = ServiceSupervisor.Stop(
                path,
                _ => null,
                _ => kills++,
                requestGracefulStop: () => { },
                gracefulWait: TimeSpan.Zero,
                wait: _ => { },
                confirmStream: plan =>
                {
                    seen = plan;
                    Assert.True(plan.StopStream);
                    Assert.False(plan.CloseProcess);
                    return ObsStreamResult.ConfirmedInactive();
                }
            );

            Assert.Equal(0, code);
            Assert.NotNull(seen);
            Assert.False(seen.CloseProcess);
            Assert.True(seen.StopStream);
            Assert.Equal(0, kills);
            Assert.False(File.Exists(path));
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    private static Harness Streaming(bool streaming = true, bool closeOwned = false)
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            Streaming = true,
        };
        return Open(
            TwitchIngestGuard.ProductionHost,
            Settings(streaming: streaming, closeOwned: closeOwned),
            socket,
            budget: Fast(0)
        );
    }

    private static Harness Open(
        string host,
        OBSSettings obs,
        FakeSession socket = null,
        FakeProcess process = null,
        ObsBackoff backoff = null,
        ObsRecordingBudget budget = null,
        Action beforeLaunch = null,
        Func<string> machineName = null
    )
    {
        socket ??= new FakeSession();
        process ??= new FakeProcess();
        var waits = new List<TimeSpan>();
        var coordinator = new ObsCoordinator(
            NullLogger.Instance,
            new AppSettings { OBS = obs },
            socket,
            process,
            new RecordingSession(NullLogger.Instance, socket, budget ?? Fast(0)),
            machineName ?? (() => host),
            backoff ?? new ObsBackoff(1, TimeSpan.Zero, TimeSpan.Zero),
            waits.Add,
            TimeSpan.FromMilliseconds(20),
            beforeLaunch
        );
        return new Harness
        {
            Coordinator = coordinator,
            Socket = socket,
            Process = process,
            Waits = waits,
        };
    }

    private static OBSSettings Settings(
        string executable = null,
        bool enabled = true,
        bool streaming = true,
        string scene = WaitingScene,
        bool closeOwned = false
    ) =>
        new()
        {
            Enabled = enabled,
            StreamingEnabled = streaming,
            ExecutablePath = executable ?? Path.Combine(Path.GetTempPath(), "not-obs64.exe"),
            WebSocketEndpoint = Endpoint,
            WebSocketPassword = "unit-password",
            WaitingSceneName = scene,
            CloseOwnedOnStop = closeOwned,
        };

    private static ObsRecordingBudget Fast(int retryCount) =>
        new()
        {
            RetryCount = retryCount,
            RetryDelay = TimeSpan.Zero,
            StartTimeout = TimeSpan.FromMilliseconds(40),
            StopTimeout = TimeSpan.FromMilliseconds(40),
            PollInterval = TimeSpan.FromMilliseconds(5),
        };

    private static int CountObs64()
    {
        Process[] found = Process.GetProcessesByName(ObsLaunchDecision.ProcessName);
        try
        {
            return found.Length;
        }
        finally
        {
            foreach (Process process in found)
            {
                process.Dispose();
            }
        }
    }

    private sealed class Harness
    {
        public ObsCoordinator Coordinator { get; init; }
        public FakeSession Socket { get; init; }
        public FakeProcess Process { get; init; }
        public List<TimeSpan> Waits { get; init; }
    }

    private sealed class FakeProcess : IObsProcess
    {
        public bool Running { get; set; }
        public bool Exists { get; set; }
        public bool Owned { get; set; }
        public int LaunchCalls { get; private set; }
        public int CloseCalls { get; private set; }

        public bool IsOwned => Owned;

        public bool IsRunning() => Running;

        public bool ExecutableExists(string path) => Exists;

        public ObsLaunchDecision Start(ObsLaunchDecision decision)
        {
            LaunchCalls++;
            return decision with { Started = false };
        }

        public void CloseOwned() => CloseCalls++;
    }

    private sealed class FakeSession : IObsSession
    {
        public bool IsIdentified { get; set; }
        public bool IsConnected { get; set; }
        public bool Recording { get; set; }
        public bool Streaming { get; set; }
        public bool KeepStreamingOnStop { get; set; }
        public bool ActivateOnStart { get; set; }
        public int ActivateOnCall { get; set; }
        public bool IdentifyOnConnect { get; set; }
        public Exception StartStreamError { get; set; }
        public Exception SelectError { get; set; }
        public string ProgramScene { get; set; }
        public string LastEndpoint { get; private set; }
        public string LastPassword { get; private set; }
        public int ConnectCalls { get; private set; }
        public int StartStreamCalls { get; private set; }
        public int StopStreamCalls { get; private set; }
        public int StartRecordCalls { get; private set; }
        public int StopRecordCalls { get; private set; }
        public int SelectCalls { get; private set; }
        public List<string> Events { get; } = new();

        public event EventHandler<ObsRecordSignal> RecordSignal;

        public void Raise(ObsRecordSignal signal) => RecordSignal?.Invoke(this, signal);

        public void Connect(string endpoint, string password, TimeSpan identifyTimeout)
        {
            ConnectCalls++;
            LastEndpoint = endpoint;
            LastPassword = password;
            if (IdentifyOnConnect)
            {
                IsIdentified = true;
                IsConnected = true;
            }
        }

        public void Disconnect()
        {
            IsConnected = false;
            IsIdentified = false;
        }

        public void SelectProgramScene(string sceneName)
        {
            SelectCalls++;
            Events.Add("scene:" + sceneName);
            if (SelectError != null)
            {
                throw SelectError;
            }

            ProgramScene = sceneName;
        }

        public bool IsRecording() => Recording;

        public void StartRecord() => StartRecordCalls++;

        public string StopRecord()
        {
            StopRecordCalls++;
            return null;
        }

        public bool IsStreamActive() => Streaming;

        public void StartStream()
        {
            StartStreamCalls++;
            Events.Add("start");
            if (StartStreamError != null)
            {
                throw StartStreamError;
            }

            if (ActivateOnStart || (ActivateOnCall > 0 && StartStreamCalls >= ActivateOnCall))
            {
                Streaming = true;
            }
        }

        public void StopStream()
        {
            StopStreamCalls++;
            Events.Add("stop");
            if (!KeepStreamingOnStop)
            {
                Streaming = false;
            }
        }
    }
}
