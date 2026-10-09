using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsDesiredStateTests
{
    private const string Endpoint = "ws://127.0.0.1:9";
    private const string WaitingScene = "waiting-screen";
    private const int ObsPid = 7012;

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
        Harness harness = Open(Settings(executable), socket, process);
        var clock = Stopwatch.StartNew();

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        clock.Stop();
        Assert.Equal(0, ObsStartedByThisProcess());
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
        Assert.Equal(
            ObsLaunchDecision.ArgumentsFor("HeroesReplay", "HeroesReplay"),
            snapshot.Launch.Arguments
        );
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
    public void Reconcile_PreflightStreamBlocker_DoesNotStartTheStreamUntilItIsFixed()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
        };
        ObsValidation result = Preflight(
            new ObsFinding(ObsValidator.StreamKeyMissing, ObsValidator.Error, "Twitch", "No key.")
        );
        int runs = 0;
        Harness harness = Open(
            Settings(),
            socket,
            preflight: () =>
            {
                runs++;
                return result;
            }
        );

        ObsRuntimeSnapshot blocked = harness.Coordinator.ReconcileStream();

        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(0, socket.SelectCalls);
        Assert.Equal(ObsOutputFailure.PreflightFailed, blocked.Stream.Failure);
        Assert.Equal(ObsValidator.StreamKeyMissing, blocked.StreamBlockedBy);

        result = Preflight();
        ObsRuntimeSnapshot started = harness.Coordinator.ReconcileStream();

        Assert.True(started.Stream.Succeeded);
        Assert.Equal(1, socket.StartStreamCalls);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void Reconcile_PreflightRunsOnceBeforeTheFirstStartStream()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
        };
        int runs = 0;
        Harness harness = Open(
            Settings(),
            socket,
            preflight: () =>
            {
                runs++;
                // An unmuted microphone is an error, but it does not stop the stream.
                return Preflight(
                    new ObsFinding(ObsValidator.MicEnabled, ObsValidator.Error, "Mic/Aux", "Mic.")
                );
            }
        );

        Assert.True(harness.Coordinator.ReconcileStream().Stream.Succeeded);
        socket.Streaming = false;
        Assert.True(harness.Coordinator.ReconcileStream().Stream.Succeeded);

        Assert.Equal(2, socket.StartStreamCalls);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void Reconcile_PreflightThatCannotRun_DoesNotStopTheStream()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
        };
        Harness harness = Open(
            Settings(),
            socket,
            preflight: () => throw new TimeoutException("OBS did not answer.")
        );

        Assert.True(harness.Coordinator.ReconcileStream().Stream.Succeeded);
        Assert.Equal(1, socket.StartStreamCalls);
    }

    private static ObsValidation Preflight(params ObsFinding[] findings) =>
        new()
        {
            Ok = findings.All(finding => finding.Severity != ObsValidator.Error),
            Findings = findings,
        };

    [Fact]
    public void BeginSession_MutesAnUnmutedMic_AfterTheCollectionIsReady()
    {
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        FakeObs obs = FakeObs.Packaged();
        obs.Mic = "Mic/Aux";
        var events = new List<string>();
        obs.Muting = name => events.Add("mute:" + name);
        Harness harness = Open(
            Settings(streaming: false),
            socket,
            microphones: obs.OpenMicrophones()
        );

        harness.Coordinator.BeginSession(() => events.Add("swap"));

        Assert.True(obs.MicMuted);
        Assert.Equal(new[] { "swap", "mute:Mic/Aux" }, events);
        // Desktop Audio, the media source, and the browser sources are never muted.
        Assert.Equal(new[] { "Mic/Aux" }, obs.MutedInputs);
        Assert.Equal(0, socket.StartStreamCalls);
    }

    [Fact]
    public void BeginSession_ObsNotIdentified_ThrowsAndMutesNothing()
    {
        FakeObs obs = FakeObs.Packaged();
        obs.Mic = "Mic/Aux";
        bool swapped = false;
        Harness harness = Open(
            Settings(streaming: false),
            new FakeSession(),
            microphones: obs.OpenMicrophones()
        );

        Assert.Throws<TimeoutException>(() =>
            harness.Coordinator.BeginSession(() => swapped = true)
        );

        Assert.False(swapped);
        Assert.Empty(obs.Requests);
    }

    [Fact]
    public void Reconcile_MutesAMicUnmutedSinceTheSessionBegan_RightBeforeStartStream()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
        };
        FakeObs obs = FakeObs.Packaged();
        obs.Mic = "Mic/Aux";
        obs.Muting = name => socket.Events.Add("mute:" + name);
        Harness harness = Open(Settings(), socket, microphones: obs.OpenMicrophones());
        harness.Coordinator.BeginSession();
        // Someone unmutes it in the OBS mixer between the session start and the stream start.
        obs.MicMuted = false;
        socket.Events.Clear();

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.True(snapshot.Stream.Succeeded);
        Assert.Equal(new[] { "scene:" + WaitingScene, "mute:Mic/Aux", "start" }, socket.Events);
        Assert.True(obs.MicMuted);
        Assert.Equal(new[] { "Mic/Aux", "Mic/Aux" }, obs.MutedInputs);
    }

    [Fact]
    public void Reconcile_AnActiveStream_IsLeftAloneWithoutAMute()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            Streaming = true,
        };
        FakeObs obs = FakeObs.Packaged();
        obs.Mic = "Mic/Aux";
        Harness harness = Open(Settings(), socket, microphones: obs.OpenMicrophones());

        Assert.True(harness.Coordinator.ReconcileStream().Stream.Succeeded);

        // The session start mutes it; a reconcile that starts nothing sends OBS nothing more.
        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Empty(obs.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMicThatCannotBeMuted_DoesNotStopTheSessionOrTheStream(bool unreadable)
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
        };
        FakeObs obs = FakeObs.Packaged();
        obs.Mic = "Mic/Aux";
        if (unreadable)
        {
            obs.Failures["GetSpecialInputs"] = new InvalidOperationException("socket closed");
        }
        else
        {
            obs.MuteRefused.Add("Mic/Aux");
        }

        Harness harness = Open(Settings(), socket, microphones: obs.OpenMicrophones());

        harness.Coordinator.BeginSession();
        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.True(snapshot.Stream.Succeeded);
        Assert.Equal(1, socket.StartStreamCalls);
        Assert.Null(snapshot.StreamBlockedBy);
        Assert.False(obs.MicMuted);
        Assert.Equal(unreadable ? 0 : 2, obs.MutedInputs.Count);
    }

    [Fact]
    public void MuteMicrophonesOff_SendsObsNoMicrophoneRequest()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
        };
        FakeObs obs = FakeObs.Packaged();
        obs.Mic = "Mic/Aux";
        Harness harness = Open(
            Settings(muteMicrophones: false),
            socket,
            microphones: obs.OpenMicrophones()
        );

        harness.Coordinator.BeginSession();
        Assert.True(harness.Coordinator.ReconcileStream().Stream.Succeeded);

        Assert.Empty(obs.Requests);
        Assert.False(obs.MicMuted);
        Assert.Equal(1, socket.StartStreamCalls);
    }

    [Fact]
    public void MuteMicrophones_IsOnByDefault()
    {
        Assert.True(new OBSSettings().MuteMicrophones);
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
        Harness harness = Open(Settings(), socket);

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
        Harness harness = Open(Settings(), socket);

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
    public void SelectScene_GameSceneMidSession_IsTheDesiredAndTheActualScene()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
        };
        Harness harness = Open(Settings(), socket);
        ObsRuntimeSnapshot started = harness.Coordinator.ReconcileStream();
        Assert.Equal(WaitingScene, started.SceneDesired);
        Assert.Equal(WaitingScene, started.SceneActual);

        harness.Coordinator.SelectScene("game-scene");

        ObsRuntimeSnapshot onAir = harness.Coordinator.State;
        Assert.Equal("game-scene", socket.ProgramScene);
        Assert.Equal("game-scene", onAir.SceneDesired);
        Assert.Equal("game-scene", onAir.SceneActual);
        Assert.True(onAir.StreamActive);
        Assert.True(onAir.Stream.Succeeded);

        // The watchdog's next reconcile finds the stream live and keeps the spectator's scene.
        ObsRuntimeSnapshot again = harness.Coordinator.ReconcileStream();
        Assert.Equal(1, socket.StartStreamCalls);
        Assert.Equal("game-scene", again.SceneDesired);
        Assert.Equal("game-scene", again.SceneActual);

        var status = new SpectatorStatus();
        ObsStatus.Copy(status, again);
        Assert.Equal("game-scene", status.ObsSceneDesired);
        Assert.Equal("game-scene", status.ObsSceneActual);
        Assert.Contains(" scene=game-scene ", ObsStatus.Describe(status), StringComparison.Ordinal);

        harness.Coordinator.SelectScene(WaitingScene);
        Assert.Equal(WaitingScene, harness.Coordinator.State.SceneDesired);
        Assert.Equal(WaitingScene, harness.Coordinator.State.SceneActual);
    }

    [Fact]
    public void SelectScene_RefusedByObs_LeavesTheStateAsItWas()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
        };
        Harness harness = Open(Settings(), socket);
        ObsRuntimeSnapshot started = harness.Coordinator.ReconcileStream();
        socket.SelectError = new InvalidOperationException("No scene named game-scene.");

        Assert.Throws<InvalidOperationException>(() =>
            harness.Coordinator.SelectScene("game-scene")
        );

        Assert.Same(started, harness.Coordinator.State);
        Assert.Equal(WaitingScene, harness.Coordinator.State.SceneActual);
    }

    [Fact]
    public void SelectScene_WithStreamingOff_StillReportsTheScene()
    {
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        Harness harness = Open(Settings(streaming: false), socket);
        Assert.Null(harness.Coordinator.State);

        harness.Coordinator.SelectScene("game-scene");

        ObsRuntimeSnapshot snapshot = harness.Coordinator.State;
        Assert.False(snapshot.StreamDesired);
        Assert.True(snapshot.WebsocketIdentified);
        Assert.Equal("game-scene", snapshot.SceneDesired);
        Assert.Equal("game-scene", snapshot.SceneActual);
        Assert.Equal(0, socket.StartStreamCalls);
    }

    [Fact]
    public void SelectScene_ReachesStatusJsonWithoutAWatchdogTick()
    {
        // Dev e2e 2026-10-08 (#357): streaming off, so the watchdog ticked every 5 minutes, and
        // status.json said prediction-report for 4 minutes while game-scene was on air.
        string path = StatusPath();
        try
        {
            var store = new SpectatorStatusStore(path);
            var socket = new FakeSession { IsIdentified = true, IsConnected = true };
            Harness harness = Open(Settings(streaming: false), socket, statusStore: store);
            harness.Coordinator.SelectScene("prediction-report");
            Assert.Equal("prediction-report", ReadStatus(path).ObsSceneActual);

            harness.Coordinator.SelectScene("game-scene");

            SpectatorStatus onAir = ReadStatus(path);
            Assert.Equal("game-scene", onAir.ObsSceneDesired);
            Assert.Equal("game-scene", onAir.ObsSceneActual);
            Assert.Equal(true, onAir.ObsWebsocketIdentified);
            Assert.Equal(false, onAir.ObsStreamDesired);
            Assert.Contains(
                " scene=game-scene ",
                ObsStatus.Describe(onAir),
                StringComparison.Ordinal
            );
        }
        finally
        {
            DeleteStatus(path);
        }
    }

    [Fact]
    public void SelectScene_SameSceneAgain_DoesNotRewriteStatusJson()
    {
        string path = StatusPath();
        try
        {
            var store = new SpectatorStatusStore(path);
            var socket = new FakeSession { IsIdentified = true, IsConnected = true };
            Harness harness = Open(Settings(streaming: false), socket, statusStore: store);
            harness.Coordinator.SelectScene("game-scene");
            Assert.True(File.Exists(path));
            File.Delete(path);

            harness.Coordinator.SelectScene("game-scene");

            // OBS got the request again, but no OBS field changed, so nothing was written.
            Assert.Equal(2, socket.SelectCalls);
            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteStatus(path);
        }
    }

    [Fact]
    public void BeginSession_WritesObsToStatusJsonAndAnUnchangedStartWritesNothing()
    {
        // The first replay of the dev e2e had every obs* field null for its first minutes (#357).
        string path = StatusPath();
        try
        {
            var store = new SpectatorStatusStore(path);
            var socket = new FakeSession
            {
                IsIdentified = true,
                IsConnected = true,
                ProgramScene = WaitingScene,
            };
            Harness harness = Open(Settings(streaming: false), socket, statusStore: store);

            harness.Coordinator.BeginSession();

            SpectatorStatus started = ReadStatus(path);
            Assert.Equal(true, started.ObsWebsocketIdentified);
            Assert.Equal(WaitingScene, started.ObsSceneActual);
            Assert.Equal(false, started.ObsStreamDesired);
            Assert.Equal(false, started.ObsStreamActive);
            Assert.Equal(WaitingScene, harness.Coordinator.State.SceneActual);

            File.Delete(path);
            harness.Coordinator.BeginSession();

            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteStatus(path);
        }
    }

    [Fact]
    public void BeginSession_ObsNotIdentified_StillWritesThatToStatusJson()
    {
        string path = StatusPath();
        try
        {
            var store = new SpectatorStatusStore(path);
            Harness harness = Open(
                Settings(streaming: false),
                new FakeSession(),
                statusStore: store
            );

            Assert.Throws<TimeoutException>(() => harness.Coordinator.BeginSession());

            SpectatorStatus status = ReadStatus(path);
            Assert.Equal(false, status.ObsWebsocketIdentified);
            Assert.Null(status.ObsSceneActual);
        }
        finally
        {
            DeleteStatus(path);
        }
    }

    [Fact]
    public void SelectScene_StatusWriteFails_TheSceneStaysOnAir()
    {
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        Harness harness = Open(
            Settings(streaming: false),
            socket,
            stateChanged: () => throw new IOException("status.json is held open.")
        );

        harness.Coordinator.SelectScene("game-scene");

        Assert.Equal(1, socket.SelectCalls);
        Assert.Equal("game-scene", harness.Coordinator.State.SceneActual);
    }

    private static string StatusPath() =>
        Path.Combine(Path.GetTempPath(), $"heroesreplay-obs-status-{Guid.NewGuid():N}.json");

    private static SpectatorStatus ReadStatus(string path) =>
        new SpectatorStatusStore(path).TryReadShared();

    private static void DeleteStatus(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reconcile_StreamingDisabled_DoesNotStart()
    {
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        int patches = 0;
        Harness harness = Open(Settings(streaming: false), socket, beforeLaunch: () => patches++);

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

    [Fact]
    public void Reconcile_ObsDisabled_DoesNotStart()
    {
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        Harness harness = Open(Settings(enabled: false), socket);

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
        Harness harness = Open(Settings(scene: " "), socket);

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
        Harness harness = Open(Settings(), socket);

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
        Harness harness = Open(Settings(), socket, process);

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

    // #395, production 2026-10-09: OBS 32.2.2 with Twitch Enhanced Broadcasting reported the
    // stream active and reconnecting, with frozen bytes, for 4 h 11 min. It counted as live.

    [Fact]
    public void Reconcile_ActiveButReconnecting_IsNotLiveInStatusJson()
    {
        var clock = new FakeClock();
        Harness harness = Open(Settings(), StuckSocket(), now: clock.Now);

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.False(snapshot.StreamActive);
        Assert.Equal(ObsStreamState.Reconnecting, snapshot.StreamHealth.State);
        Assert.Equal(ObsOutputFailure.Waiting, snapshot.Stream.Failure);
        Assert.Null(snapshot.StreamBlockedBy);
        Assert.False(harness.Coordinator.ReadStreamHealth().IsLive);
        var status = new SpectatorStatus();
        Assert.True(ObsStatus.Copy(status, snapshot));
        Assert.Equal(false, status.ObsStreamActive);
        Assert.Equal("Reconnecting", status.ObsStreamState);
        Assert.Equal(true, status.ObsStreamReconnecting);
        Assert.Equal(clock.Start, status.ObsStreamStuckSince);
        Assert.Contains(
            " active=False state=Reconnecting since=2026-10-09T05:51:48Z",
            ObsStatus.Describe(status),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Reconcile_ReconnectingUnderTheThreshold_LeavesObsAlone()
    {
        var clock = new FakeClock();
        FakeSession socket = StuckSocket();
        Harness harness = Open(Settings(), socket, now: clock.Now);

        harness.Coordinator.ReconcileStream();
        clock.Advance(ObsStreamRecovery.DefaultStuckAfter - TimeSpan.FromSeconds(1));
        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.Empty(socket.Events);
        Assert.Equal(0, socket.StopStreamCalls);
        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(ObsOutputFailure.Waiting, snapshot.Stream.Failure);
        Assert.Equal(TimeSpan.FromSeconds(90), Settings().StreamStuckAfter);
    }

    [Fact]
    public void Reconcile_ReconnectingPastTheThreshold_StopsAndStartsExactlyOnce()
    {
        var clock = new FakeClock();
        FakeSession socket = StuckSocket();
        Harness harness = Open(Settings(), socket, now: clock.Now);

        harness.Coordinator.ReconcileStream();
        clock.Advance(ObsStreamRecovery.DefaultStuckAfter);
        ObsRuntimeSnapshot restarted = harness.Coordinator.ReconcileStream();
        clock.Advance(TimeSpan.FromSeconds(15));
        ObsRuntimeSnapshot again = harness.Coordinator.ReconcileStream();

        Assert.Equal(1, socket.StopStreamCalls);
        Assert.Equal(1, socket.StartStreamCalls);
        Assert.Equal(new[] { "stop", "scene:" + WaitingScene, "start" }, socket.Events);
        Assert.True(restarted.Stream.Succeeded);
        Assert.True(restarted.StreamActive);
        Assert.Equal(ObsStreamState.Live, restarted.StreamHealth.State);
        Assert.True(again.StreamActive);
        Assert.True(again.Stream.Succeeded);
    }

    [Fact]
    public void Reconcile_RestartMidMatch_NeverSelectsTheWaitingScene()
    {
        // #407, ASA-SERVER 2026-10-09 12:46:38: the restart mid-match switched to the waiting
        // scene, its put-back timed out, and the waiting scene stayed on air for the match.
        var clock = new FakeClock();
        FakeSession socket = StuckSocket();
        Harness harness = Open(Settings(), socket, now: clock.Now);
        harness.Coordinator.SelectScene("game-scene");

        harness.Coordinator.ReconcileStream();
        clock.Advance(ObsStreamRecovery.DefaultStuckAfter);
        ObsRuntimeSnapshot restarted = harness.Coordinator.ReconcileStream();

        Assert.Equal(new[] { "scene:game-scene", "stop", "start" }, socket.Events);
        Assert.DoesNotContain("scene:" + WaitingScene, socket.Events);
        Assert.Equal("game-scene", socket.ProgramScene);
        Assert.Equal("game-scene", restarted.SceneDesired);
        Assert.Equal("game-scene", restarted.SceneActual);
        Assert.True(restarted.StreamActive);
    }

    [Fact]
    public void Reconcile_StartMidMatch_StartsOnGameScene()
    {
        // Production 2026-10-09 11:03:02: after a StopStream by hand, the reconcile started the
        // stream on waiting-screen mid-match, and it stayed on air for the last 21 minutes.
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
        };
        Harness harness = Open(Settings(), socket);
        harness.Coordinator.SelectScene("game-scene");

        ObsRuntimeSnapshot started = harness.Coordinator.ReconcileStream();

        Assert.Equal(new[] { "scene:game-scene", "start" }, socket.Events);
        Assert.Equal("game-scene", socket.ProgramScene);
        Assert.Equal("game-scene", started.SceneDesired);
        Assert.Equal("game-scene", started.SceneActual);
        Assert.True(started.StreamActive);
    }

    [Fact]
    public void Reconcile_StartThatFailsMidMatch_KeepsGameSceneOnAir()
    {
        // A recording takes the program output too: a failed start must not put the waiting
        // scene in the match's recording.
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        Harness harness = Open(Settings(), socket);
        harness.Coordinator.SelectScene("game-scene");

        ObsRuntimeSnapshot failed = harness.Coordinator.ReconcileStream();

        Assert.False(failed.Stream.Succeeded);
        Assert.Equal(new[] { "scene:game-scene", "start" }, socket.Events);
        Assert.Equal("game-scene", socket.ProgramScene);
        Assert.Equal("game-scene", failed.SceneActual);
    }

    [Fact]
    public void Reconcile_StartWithNoSceneAskedFor_ShowsTheWaitingSceneFirst()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
            ProgramScene = "game-scene",
        };
        Harness harness = Open(Settings(), socket);

        ObsRuntimeSnapshot started = harness.Coordinator.ReconcileStream();

        Assert.Equal(new[] { "scene:" + WaitingScene, "start" }, socket.Events);
        Assert.Equal(WaitingScene, socket.ProgramScene);
        Assert.Equal(WaitingScene, started.SceneDesired);
        Assert.True(started.StreamActive);

        // The start did not make the waiting scene the spectator's: nothing to put back.
        Assert.False(harness.Coordinator.ReconcileScene());
        Assert.Equal(2, socket.Events.Count);
    }

    [Fact]
    public void Reconcile_ObsFoundAgainOnItsSavedScene_PutsGameSceneBackBeforeTheStart()
    {
        // #407: OBS killed and started again mid-match came back on its saved scene,
        // waiting-screen. The stream went live, and nothing put game-scene back.
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            Streaming = true,
        };
        var logger = new ListLogger();
        Harness harness = Open(Settings(), socket, logger: logger);
        harness.Coordinator.SelectScene("game-scene");
        harness.Coordinator.ReconcileStream();

        // A new OBS: the websocket dropped, the output is inactive, the saved scene is on air.
        socket.Disconnect();
        socket.Streaming = false;
        socket.ProgramScene = WaitingScene;
        socket.IdentifyOnConnect = true;
        socket.ActivateOnStart = true;
        socket.Events.Clear();
        ObsRuntimeSnapshot started = harness.Coordinator.ReconcileStream();

        Assert.Equal(new[] { "scene:game-scene", "start" }, socket.Events);
        Assert.Equal("game-scene", socket.ProgramScene);
        Assert.Equal("game-scene", started.SceneActual);
        Assert.True(started.StreamActive);
        Assert.Single(
            logger.Lines,
            line =>
                line.Level == LogLevel.Information
                && line.Message.Contains("put game-scene back", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ReconcileScene_AFailedSwitch_IsCorrectedOnTheNextTick()
    {
        // The put-back at 12:47:28 timed out (OBS busy). The spectator still wants game-scene.
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            Streaming = true,
            ProgramScene = WaitingScene,
        };
        var logger = new ListLogger();
        Harness harness = Open(Settings(), socket, logger: logger);
        harness.Coordinator.ReconcileStream();
        socket.SelectError = new InvalidOperationException("Request timed out");
        Assert.Throws<InvalidOperationException>(() =>
            harness.Coordinator.SelectScene("game-scene")
        );
        Assert.False(harness.Coordinator.ReconcileScene());
        int warnings = logger.Lines.Count(line => line.Level == LogLevel.Warning);

        // Still failing: no second warning for the same scene.
        Assert.False(harness.Coordinator.ReconcileScene());
        Assert.Equal(warnings, logger.Lines.Count(line => line.Level == LogLevel.Warning));

        socket.SelectError = null;
        Assert.True(harness.Coordinator.ReconcileScene());

        Assert.Equal("game-scene", socket.ProgramScene);
        Assert.Equal("game-scene", harness.Coordinator.State.SceneDesired);
        Assert.Equal("game-scene", harness.Coordinator.State.SceneActual);
        Assert.Single(
            logger.Lines,
            line =>
                line.Level == LogLevel.Information
                && line.Message
                    == "OBS showed waiting-screen, not the spectator's scene: put game-scene back on the program output."
        );

        // On air now: the next tick sends nothing.
        int selects = socket.SelectCalls;
        Assert.False(harness.Coordinator.ReconcileScene());
        Assert.Equal(selects, socket.SelectCalls);
    }

    [Fact]
    public void ReconcileScene_ASceneChangedInTheObsUi_GoesBack()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            Streaming = true,
        };
        Harness harness = Open(Settings(), socket);
        harness.Coordinator.SelectScene("match-report");
        socket.ProgramScene = "game-scene";

        Assert.True(harness.Coordinator.ReconcileScene());

        Assert.Equal("match-report", socket.ProgramScene);
    }

    [Fact]
    public void ReconcileScene_BeforeTheSpectatorAskedOrWithoutObs_DoesNothing()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ProgramScene = "game-scene",
        };
        Harness harness = Open(Settings(), socket);

        Assert.False(harness.Coordinator.ReconcileScene());

        harness.Coordinator.SelectScene(WaitingScene);
        socket.Disconnect();
        socket.ProgramScene = "game-scene";
        Assert.False(harness.Coordinator.ReconcileScene());
        Assert.Equal(1, socket.SelectCalls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ReconcileScene_WithoutADesiredStream_DoesNothing(bool enabled, bool streaming)
    {
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        Harness harness = Open(Settings(enabled: enabled, streaming: streaming), socket);
        harness.Coordinator.SelectScene("game-scene");
        socket.ProgramScene = WaitingScene;

        Assert.False(harness.Coordinator.ReconcileScene());
        Assert.Equal(WaitingScene, socket.ProgramScene);
    }

    [Fact]
    public void ReconcileScene_WhileTheSpectatorSwitches_SkipsTheTick()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ProgramScene = WaitingScene,
        };
        Harness harness = Open(Settings(), socket);
        harness.Coordinator.SelectScene("game-scene");
        bool? midSwitch = null;
        // The watchdog's tick on its own thread (a Task could run inline on this one).
        socket.WhileSelecting = () =>
        {
            var tick = new Thread(() => midSwitch = harness.Coordinator.ReconcileScene());
            tick.Start();
            tick.Join();
        };

        harness.Coordinator.SelectScene("match-report");

        Assert.False(midSwitch);
        Assert.Equal("match-report", socket.ProgramScene);
    }

    [Fact]
    public void WantScene_WithoutObs_IsWhatAStartLaterKeepsOnAir()
    {
        // A replay spectated without OBS (BeginSession failed): the game scene was skipped. A
        // stream the reconcile starts once OBS answers must not show the waiting scene.
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
            ProgramScene = WaitingScene,
        };
        Harness harness = Open(Settings(), socket);
        harness.Coordinator.WantScene("game-scene");

        ObsRuntimeSnapshot started = harness.Coordinator.ReconcileStream();

        Assert.Equal(new[] { "scene:game-scene", "start" }, socket.Events);
        Assert.Equal("game-scene", started.SceneDesired);
        Assert.Equal("game-scene", started.SceneActual);
    }

    [Fact]
    public void Reconcile_SceneTheSpectatorChoseDuringTheStart_IsNotOverwritten()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
        };
        Harness harness = Open(Settings(), socket);
        harness.Coordinator.SelectScene("game-scene");
        socket.WhileStarting = () => harness.Coordinator.SelectScene("match-report");

        ObsRuntimeSnapshot started = harness.Coordinator.ReconcileStream();

        Assert.Equal("match-report", socket.ProgramScene);
        Assert.Equal("match-report", started.SceneDesired);
        Assert.Equal("match-report", started.SceneActual);
    }

    [Fact]
    public void Reconcile_SpectatorOnTheWaitingScene_StaysThere()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
        };
        Harness harness = Open(Settings(), socket);
        harness.Coordinator.SelectScene(WaitingScene);

        ObsRuntimeSnapshot started = harness.Coordinator.ReconcileStream();

        Assert.Equal(
            new[] { "scene:" + WaitingScene, "scene:" + WaitingScene, "start" },
            socket.Events
        );
        Assert.Equal(WaitingScene, started.SceneActual);
    }

    [Fact]
    public void Reconcile_StreamStuckAfter_IsTheConfiguredThreshold()
    {
        var clock = new FakeClock();
        FakeSession socket = StuckSocket();
        OBSSettings obs = Settings();
        obs.StreamStuckAfter = TimeSpan.FromSeconds(30);
        Harness harness = Open(obs, socket, now: clock.Now);

        harness.Coordinator.ReconcileStream();
        clock.Advance(TimeSpan.FromSeconds(29));
        harness.Coordinator.ReconcileStream();
        int stopsBefore = socket.StopStreamCalls;
        clock.Advance(TimeSpan.FromSeconds(1));
        harness.Coordinator.ReconcileStream();

        Assert.Equal(0, stopsBefore);
        Assert.Equal(1, socket.StopStreamCalls);
        Assert.Equal(1, socket.StartStreamCalls);
    }

    [Fact]
    public void Reconcile_FrozenBytesPastTheThreshold_CountAsStalled()
    {
        var clock = new FakeClock();
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            Streaming = true,
            FreezeBytes = true,
            Bytes = 199_373_948_174,
            ActivateOnStart = true,
            ProgramScene = "game-scene",
        };
        Harness harness = Open(Settings(), socket, now: clock.Now);

        ObsRuntimeSnapshot first = harness.Coordinator.ReconcileStream();
        clock.Advance(ObsStreamHealth.StallWindow);
        ObsRuntimeSnapshot stalled = harness.Coordinator.ReconcileStream();
        int stopsWhileStalled = socket.StopStreamCalls;
        clock.Advance(ObsStreamRecovery.DefaultStuckAfter);
        ObsRuntimeSnapshot restarted = harness.Coordinator.ReconcileStream();

        // One read cannot tell frozen bytes from moving ones.
        Assert.True(first.StreamActive);
        Assert.Equal(ObsStreamState.Stalled, stalled.StreamHealth.State);
        Assert.Equal(clock.Start, stalled.StreamHealth.StuckSince);
        Assert.False(stalled.StreamActive);
        Assert.Equal(0, stopsWhileStalled);
        Assert.Equal(1, socket.StopStreamCalls);
        Assert.Equal(1, socket.StartStreamCalls);
        Assert.True(restarted.StreamActive);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Reconcile_AStuckStreamOnABoxThatDoesNotStream_IsNeverStoppedOrStarted(
        bool streaming,
        bool armed
    )
    {
        var clock = new FakeClock();
        FakeSession socket = StuckSocket();
        Harness harness = Open(
            Settings(streaming: streaming),
            socket,
            armed: () => armed,
            now: clock.Now
        );

        harness.Coordinator.ReconcileStream();
        clock.Advance(TimeSpan.FromHours(4));
        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.Empty(socket.Events);
        Assert.Equal(0, socket.StopStreamCalls);
        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(ObsOutputFailure.NotRequested, snapshot.Stream.Failure);
    }

    [Fact]
    public void Reconcile_FailedRestart_BacksOffOneTwoThenFiveMinutes()
    {
        var clock = new FakeClock();
        FakeSession socket = StuckSocket();
        socket.ActivateOnStart = false;
        socket.RejectOnStart = true;
        Harness harness = Open(Settings(), socket, now: clock.Now);

        harness.Coordinator.ReconcileStream();
        clock.Advance(ObsStreamRecovery.DefaultStuckAfter);
        ObsRuntimeSnapshot failed = harness.Coordinator.ReconcileStream();

        Assert.Equal(1, socket.StopStreamCalls);
        Assert.Equal(1, socket.StartStreamCalls);
        Assert.False(failed.Stream.Succeeded);
        Assert.False(failed.StreamActive);
        TimeSpan[] delays =
        [
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5),
        ];
        foreach (TimeSpan delay in delays)
        {
            int starts = socket.StartStreamCalls;
            clock.Advance(delay - TimeSpan.FromSeconds(1));
            ObsRuntimeSnapshot waiting = harness.Coordinator.ReconcileStream();
            Assert.Equal(starts, socket.StartStreamCalls);
            Assert.Equal(ObsOutputFailure.Waiting, waiting.Stream.Failure);
            clock.Advance(TimeSpan.FromSeconds(1));
            harness.Coordinator.ReconcileStream();
            Assert.Equal(starts + 1, socket.StartStreamCalls);
        }

        // Stopped once: later attempts start the output the restart left inactive.
        Assert.Equal(1, socket.StopStreamCalls);
    }

    [Fact]
    public void Reconcile_ARestartThatComesBackReconnecting_IsAFailedAttempt()
    {
        var clock = new FakeClock();
        FakeSession socket = StuckSocket();
        socket.ReconnectOnStart = true;
        Harness harness = Open(Settings(), socket, now: clock.Now);

        harness.Coordinator.ReconcileStream();
        clock.Advance(ObsStreamRecovery.DefaultStuckAfter);
        ObsRuntimeSnapshot restarted = harness.Coordinator.ReconcileStream();
        clock.Advance(TimeSpan.FromSeconds(30));
        ObsRuntimeSnapshot waiting = harness.Coordinator.ReconcileStream();

        Assert.False(restarted.StreamActive);
        Assert.Equal(ObsStreamState.Reconnecting, restarted.StreamHealth.State);
        // The new output's own stretch began at the restart, and the backoff holds the next one.
        Assert.Equal(
            clock.Start + ObsStreamRecovery.DefaultStuckAfter,
            restarted.StreamHealth.StuckSince
        );
        Assert.Equal(ObsOutputFailure.Waiting, waiting.Stream.Failure);
        Assert.Equal(1, socket.StopStreamCalls);
        Assert.Equal(1, socket.StartStreamCalls);
    }

    [Fact]
    public void Reconcile_AStopThatIsNotConfirmed_DoesNotStartAndBacksOff()
    {
        var clock = new FakeClock();
        FakeSession socket = StuckSocket();
        socket.KeepStreamingOnStop = true;
        Harness harness = Open(Settings(), socket, now: clock.Now);

        harness.Coordinator.ReconcileStream();
        clock.Advance(ObsStreamRecovery.DefaultStuckAfter);
        ObsRuntimeSnapshot refused = harness.Coordinator.ReconcileStream();
        clock.Advance(TimeSpan.FromSeconds(59));
        harness.Coordinator.ReconcileStream();
        int stopsDuringBackoff = socket.StopStreamCalls;
        clock.Advance(TimeSpan.FromSeconds(1));
        harness.Coordinator.ReconcileStream();

        Assert.False(refused.Stream.Succeeded);
        Assert.Equal(ObsStreamHealth.StuckReconnectingCode, refused.Stream.Reason);
        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(1, stopsDuringBackoff);
        Assert.Equal(2, socket.StopStreamCalls);
    }

    [Fact]
    public void Reconcile_StreamStatusNotRead_StartsAndStopsNothing()
    {
        FakeSession socket = StuckSocket();
        socket.StreamReadError = new TimeoutException("GetStreamStatus timed out.");
        Harness harness = Open(Settings(), socket);

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.Empty(socket.Events);
        Assert.Equal(ObsStreamState.Unknown, snapshot.StreamHealth.State);
        Assert.False(snapshot.StreamActive);
        Assert.Equal(ObsOutputFailure.NotConfirmed, snapshot.Stream.Failure);
        Assert.Contains("timed out", snapshot.Stream.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Shutdown_StopsAStreamStuckReconnecting()
    {
        FakeSession socket = StuckSocket();
        Harness harness = Open(Settings(), socket);

        ObsShutdownResult result = harness.Coordinator.Shutdown();

        Assert.True(result.Stream.Succeeded);
        Assert.False(result.Stream.Active);
        Assert.Equal(1, socket.StopStreamCalls);
        Assert.Equal(ObsStreamState.Inactive, harness.Coordinator.State.StreamHealth.State);
    }

    private static FakeSession StuckSocket() =>
        new()
        {
            IsIdentified = true,
            IsConnected = true,
            Streaming = true,
            Reconnecting = true,
            Bytes = 199_373_948_174,
            ActivateOnStart = true,
            ProgramScene = "game-scene",
        };

    /// <summary>A clock that moves only when a test says so.</summary>
    private sealed class FakeClock
    {
        private TimeSpan elapsed;

        public DateTimeOffset Start { get; } = new(2026, 10, 9, 5, 51, 48, TimeSpan.Zero);

        public DateTimeOffset Now() => Start + elapsed;

        public void Advance(TimeSpan by) => elapsed += by;
    }

    [Fact]
    public void Reconcile_SendsStartStreamOncePerAttempt()
    {
        // #407: one restart attempt sent StartStream 4 times, 9 to 12 s apart, and the fourth
        // hung OBS. Now one attempt is one StartStream; the next waits for the backoff.
        var clock = new FakeClock();
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            RejectOnStart = true,
        };
        Harness harness = Open(Settings(), socket, budget: Fast(3), now: clock.Now);

        ObsRuntimeSnapshot failed = harness.Coordinator.ReconcileStream();

        Assert.Equal(1, socket.StartStreamCalls);
        Assert.False(failed.Stream.Succeeded);
        Assert.Contains("stopped", failed.Stream.Detail, StringComparison.Ordinal);
        Assert.Empty(harness.Waits);

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(
            ObsOutputFailure.Waiting,
            harness.Coordinator.ReconcileStream().Stream.Failure
        );
        Assert.Equal(1, socket.StartStreamCalls);

        socket.RejectOnStart = false;
        socket.ActivateOnStart = true;
        clock.Advance(TimeSpan.FromSeconds(1));
        ObsRuntimeSnapshot live = harness.Coordinator.ReconcileStream();

        Assert.Equal(2, socket.StartStreamCalls);
        Assert.True(live.StreamActive);
    }

    [Fact]
    public void Reconcile_AStartThatNeverEnded_GetsNoSecondStartStream()
    {
        // OBS reported neither started nor stopped within the start timeout: the start may still
        // run (building encoders on OBS's UI thread), so the next attempt sends nothing.
        var clock = new FakeClock();
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        Harness harness = Open(Settings(), socket, now: clock.Now);

        harness.Coordinator.ReconcileStream();
        clock.Advance(TimeSpan.FromMinutes(1));
        ObsRuntimeSnapshot blocked = harness.Coordinator.ReconcileStream();

        Assert.Equal(1, socket.StartStreamCalls);
        Assert.Contains("has not ended", blocked.Stream.Detail, StringComparison.Ordinal);

        // OBS reports the first start over: the next attempt may send one again.
        socket.Raise(ObsRecordSignal.Stream(ObsRecordSignalKind.StreamStarting));
        socket.Raise(ObsRecordSignal.Stream(ObsRecordSignalKind.StreamStopped));
        socket.ActivateOnStart = true;
        clock.Advance(TimeSpan.FromMinutes(2));
        ObsRuntimeSnapshot live = harness.Coordinator.ReconcileStream();

        Assert.Equal(2, socket.StartStreamCalls);
        Assert.True(live.StreamActive);
    }

    [Fact]
    public void StartStream_ALateStoppedSignalFromTheEarlierStop_IsNotTheStartsFailure()
    {
        // A late OBS_WEBSOCKET_OUTPUT_STOPPED from the restart's StopStream arrives after the
        // StartStream, before OBS reports this start starting: it must not end the wait.
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        socket.WhileStarting = () =>
            socket.Raise(ObsRecordSignal.Stream(ObsRecordSignalKind.StreamStopped));
        var session = new RecordingSession(NullLogger.Instance, socket, Fast(0));

        ObsStreamResult result = session.StartStreaming(() => { });

        Assert.False(result.Succeeded);
        Assert.Contains("within", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Reconcile_UnreachableIngest_SendsNoStartStreamAndBacksOff()
    {
        // #407: the relay to the ingest was down. StartStream would have built 5 encoders and
        // opened a "Failed to connect" dialog for nothing.
        var clock = new FakeClock();
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
            Server = new ObsStreamServer("rtmp_custom", null, "rtmp://127.0.0.1:19350/app"),
        };
        var ingest = new FakeIngest { Failure = "ConnectionRefused" };
        var logger = new ListLogger();
        Harness harness = Open(Settings(), socket, now: clock.Now, ingest: ingest, logger: logger);
        harness.Coordinator.SelectScene("game-scene");
        socket.Events.Clear();

        ObsRuntimeSnapshot refused = harness.Coordinator.ReconcileStream();

        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Empty(socket.Events);
        Assert.Equal(new[] { "127.0.0.1:19350 3s" }, ingest.Connects);
        Assert.Equal(ObsOutputFailure.IngestUnreachable, refused.Stream.Failure);
        Assert.Equal(ObsIngest.UnreachableCode, refused.Stream.Reason);
        Assert.Equal(ObsIngest.UnreachableCode, refused.StreamBlockedBy);
        Assert.Single(
            logger.Lines,
            line =>
                line.Level == LogLevel.Warning
                && line.Message.StartsWith(
                    "OBS stream was not started (obs.ingest_unreachable). The ingest 127.0.0.1:19350 did not accept a TCP connection within 3 s (ConnectionRefused), so StartStream was not sent. Next attempt in 00:01:00.",
                    StringComparison.Ordinal
                )
        );

        // A failed attempt: the next one waits for the backoff, and then needs the ingest.
        clock.Advance(TimeSpan.FromSeconds(30));
        harness.Coordinator.ReconcileStream();
        Assert.Single(ingest.Connects);
        ingest.Failure = null;
        clock.Advance(TimeSpan.FromSeconds(30));
        ObsRuntimeSnapshot live = harness.Coordinator.ReconcileStream();

        Assert.Equal(1, socket.StartStreamCalls);
        Assert.True(live.StreamActive);
        Assert.Equal("game-scene", socket.ProgramScene);
    }

    [Fact]
    public void Reconcile_IngestCheckOff_OrServerItCannotName_StillStarts()
    {
        var off = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
        };
        OBSSettings obs = Settings();
        obs.IngestPreflightTimeout = TimeSpan.Zero;
        var ingest = new FakeIngest { Failure = "refused" };
        Assert.True(Open(obs, off, ingest: ingest).Coordinator.ReconcileStream().StreamActive);
        Assert.Empty(ingest.Connects);

        var srt = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
            Server = new ObsStreamServer("rtmp_custom", null, "srt://127.0.0.1:9000"),
        };
        Assert.True(
            Open(Settings(), srt, ingest: ingest).Coordinator.ReconcileStream().StreamActive
        );
        Assert.Empty(ingest.Connects);
    }

    [Fact]
    public void Reconcile_FailedStart_ClosesOnlyObsConnectFailDialog()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            RejectOnStart = true,
        };
        var windows = new FakeWindows();
        // OBS opens its dialog after it reports the stream stopped.
        socket.WhileStarting = () =>
        {
            windows.Add(ObsPid, "Failed to connect");
            windows.Add(ObsPid, "OBS 32.2.2 - Profile: HeroesReplay - Scenes: HeroesReplay");
            windows.Add(ObsPid + 1, "Failed to connect");
        };
        var logger = new ListLogger();
        Harness harness = Open(Settings(), socket, windows: windows, logger: logger);

        harness.Coordinator.ReconcileStream();

        Assert.Equal(new[] { windows.Windows[0].Handle }, windows.Closed);
        Assert.Single(
            logger.Lines,
            line =>
                line.Level == LogLevel.Information
                && line.Message.StartsWith(
                    "Closed OBS's \"Failed to connect\" dialog",
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public void Reconcile_FailedStartWhileObsDoesNotAnswer_LeavesItsDialogs()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            RejectOnStart = true,
        };
        var windows = new FakeWindows();
        socket.WhileStarting = () =>
        {
            windows.Add(ObsPid, "Failed to connect");
            socket.StreamReadError = new TimeoutException("Request timed out");
        };
        Harness harness = Open(Settings(), socket, windows: windows);

        harness.Coordinator.ReconcileStream();

        Assert.Empty(windows.Closed);
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
        Harness harness = Open(Settings(), socket, process);

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
    public void StartStream_AnExceptionOnceItWasSent_IsNotRetried()
    {
        // ASA-SERVER 2026-10-09: StartStream answered "Request timed out" while OBS still built
        // its encoders. Sending it again stacks a second start on the first (#407).
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            StartStreamError = new InvalidOperationException("Request timed out"),
        };
        var session = new RecordingSession(NullLogger.Instance, socket, Fast(3));

        ObsStreamResult result = session.StartStreaming(() => { });

        Assert.Equal(1, socket.StartStreamCalls);
        Assert.False(result.Succeeded);
        Assert.False(result.Active);
        Assert.Equal(ObsOutputFailure.RequestError, result.Failure);
        Assert.Equal("Request timed out", result.Detail);
    }

    [Fact]
    public void StartStream_AnExceptionBeforeItWasSent_IsRetried()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
            StreamActiveErrors = 1,
        };
        var session = new RecordingSession(NullLogger.Instance, socket, Fast(1));

        ObsStreamResult result = session.StartStreaming(() => { });

        Assert.Equal(1, socket.StartStreamCalls);
        Assert.True(result.Succeeded);
        Assert.True(result.Active);
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
        Harness harness = Open(Settings(streaming: false), socket);

        ObsRecordingResult started = harness.Coordinator.StartRecording(() => true, 7, "unit");
        ObsRecordingResult stopped = harness.Coordinator.StopRecording(7);

        Assert.False(started.Succeeded);
        Assert.False(started.Owned);
        Assert.Equal(ObsOutputFailure.AlreadyRecording, started.Failure);
        Assert.Equal(0, socket.StartRecordCalls);
        Assert.Equal(ObsOutputFailure.NotOwned, stopped.Failure);
        Assert.Null(stopped.OutputPath);
        Assert.Equal(1, socket.StopRecordCalls);
        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(0, harness.Process.LaunchCalls);
    }

    /// <summary>
    /// #318: with OBS:Enabled=false and OBS:RecordingEnabled=true, the recording start and the
    /// session end send OBS nothing, not even a connect.
    /// </summary>
    [Fact]
    public void Recording_ObsDisabled_SendsObsNothing()
    {
        var socket = new FakeSession { IdentifyOnConnect = true, RecordOnStart = true };
        OBSSettings obs = Settings(enabled: false, streaming: false);
        obs.RecordingEnabled = true;
        Harness harness = Open(obs, socket);
        var replay = new LoadedReplay { ReplayId = 65820711, PolicyAllowsRecording = true };

        ObsRecordingResult started = harness.Coordinator.StartRecording(
            () => SessionMedia.ShouldRecord(obs, replay),
            65820711,
            "unit"
        );
        ObsRecordingResult stopped = harness.Coordinator.StopRecording(65820711);

        Assert.Equal(ObsOutputFailure.NotRequested, started.Failure);
        Assert.False(started.Owned);
        Assert.Equal(ObsOutputFailure.NotOwned, stopped.Failure);
        Assert.Equal(0, socket.ConnectCalls);
        Assert.Equal(0, socket.StartRecordCalls);
        Assert.Equal(0, socket.StopRecordCalls);
        Assert.Equal(0, socket.StopStreamCalls);
        Assert.False(socket.Recording);
        Assert.Equal(0, harness.Process.LaunchCalls);
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

    [Fact]
    public void LaunchArguments_AreOnlyTheProfileAndCollection()
    {
        // OBS 32.2.2 has no --disable-shutdown-check; the crash dialog is avoided by clearing
        // its stale run sentinel instead (ObsCrashSentinel).
        string arguments = ObsLaunchDecision.ArgumentsFor("HeroesReplay", "HeroesReplay");

        Assert.Equal("--profile \"HeroesReplay\" --collection \"HeroesReplay\"", arguments);
        foreach (
            string flag in new[]
            {
                "--disable-shutdown-check",
                "--disable-missing-files-check",
                "--disable-updater",
                "--minimize-to-tray",
                "--safe-mode",
                "--startstreaming",
            }
        )
        {
            Assert.DoesNotContain(flag, arguments, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EnsureIdentified_OwnLaunch_ClearsStaleSentinelsFirst()
    {
        string directory = NewSentinelDirectory();
        try
        {
            string stale = Path.Combine(directory, "run_81b110f2-0000-0000-0000-000000000000");
            File.WriteAllText(stale, "");
            var socket = new FakeSession { IdentifyOnConnect = true };
            var process = new FakeProcess { Exists = true, StartSucceeds = true };
            bool sentinelAtStart = true;
            process.OnStart = () => sentinelAtStart = File.Exists(stale);
            StartupHarness harness = OpenStartup(
                socket,
                process,
                TimeSpan.FromSeconds(60),
                new ObsCrashSentinel(directory, () => process.Running, NullLogger.Instance)
            );

            harness.Coordinator.EnsureIdentified();

            Assert.Equal(1, process.LaunchCalls);
            Assert.False(sentinelAtStart);
            Assert.False(File.Exists(stale));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EnsureIdentified_ObsAlreadyRunning_LeavesTheSentinel()
    {
        string directory = NewSentinelDirectory();
        try
        {
            string live = Path.Combine(directory, "run_b0c325dc-0000-0000-0000-000000000000");
            File.WriteAllText(live, "");
            var socket = new FakeSession { IdentifyOnConnect = true };
            var process = new FakeProcess { Exists = true, Running = true };
            StartupHarness harness = OpenStartup(
                socket,
                process,
                TimeSpan.FromSeconds(60),
                new ObsCrashSentinel(directory, () => process.Running, NullLogger.Instance)
            );

            harness.Coordinator.EnsureIdentified();

            Assert.Equal(0, process.LaunchCalls);
            Assert.True(File.Exists(live));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string NewSentinelDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hr-obs-sentinel-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void EnsureIdentified_AfterOwnLaunch_RetriesUntilObsIdentifies()
    {
        var socket = new FakeSession { IdentifyOnConnectCall = 4 };
        var process = new FakeProcess { Exists = true, StartSucceeds = true };
        StartupHarness harness = OpenStartup(socket, process, TimeSpan.FromSeconds(60));

        harness.Coordinator.EnsureIdentified();

        Assert.True(socket.IsIdentified);
        Assert.Equal(1, process.LaunchCalls);
        Assert.Equal(
            ObsLaunchDecision.ArgumentsFor("HeroesReplay", "HeroesReplay"),
            process.LastStart.Arguments
        );
        Assert.Equal(4, socket.ConnectCalls);
        Assert.Equal(
            new[]
            {
                ObsCoordinator.LaunchSettle,
                ObsCoordinator.StartupRetryPause,
                ObsCoordinator.StartupRetryPause,
                ObsCoordinator.StartupRetryPause,
            },
            harness.Waits
        );
        Assert.True(harness.Elapsed < TimeSpan.FromSeconds(60), harness.Elapsed.ToString());
    }

    [Fact]
    public void EnsureIdentified_AfterOwnLaunch_GivesUpAtTheStartupDeadline()
    {
        var socket = new FakeSession();
        var process = new FakeProcess { Exists = true, StartSucceeds = true };
        StartupHarness harness = OpenStartup(socket, process, TimeSpan.FromSeconds(60));

        TimeoutException failed = Assert.Throws<TimeoutException>(
            harness.Coordinator.EnsureIdentified
        );

        Assert.False(socket.IsIdentified);
        Assert.Equal(1, process.LaunchCalls);
        // 5 s settle, then 10 s attempts with 2 s pauses until 60 s: more than the old one attempt.
        Assert.True(socket.ConnectCalls > 1, socket.ConnectCalls.ToString());
        Assert.Equal(TimeSpan.FromSeconds(60), harness.Elapsed);
        Assert.All(socket.ConnectTimeouts, t => Assert.True(t <= TimeSpan.FromSeconds(10)));
        Assert.Contains("did not identify within", failed.Message, StringComparison.Ordinal);

        // The window is over: OBS now runs, so the next call is one attempt and no launch.
        int before = socket.ConnectCalls;
        Assert.Throws<TimeoutException>(harness.Coordinator.EnsureIdentified);
        Assert.Equal(before + 1, socket.ConnectCalls);
        Assert.Equal(1, process.LaunchCalls);
    }

    [Fact]
    public void EnsureIdentified_ObsAlreadyRunning_MakesOneAttempt()
    {
        var socket = new FakeSession { IdentifyOnConnectCall = 2 };
        var process = new FakeProcess { Exists = true, Running = true };
        StartupHarness harness = OpenStartup(socket, process, TimeSpan.FromSeconds(60));

        Assert.Throws<TimeoutException>(harness.Coordinator.EnsureIdentified);

        Assert.Equal(1, socket.ConnectCalls);
        Assert.Equal(0, process.LaunchCalls);
        Assert.Empty(harness.Waits);
        Assert.Equal(TimeSpan.FromSeconds(10), harness.Elapsed);
    }

    [Fact]
    public void EnsureIdentified_LaunchThatDidNotStart_MakesOneAttempt()
    {
        var socket = new FakeSession();
        var process = new FakeProcess { Exists = true, StartSucceeds = false };
        StartupHarness harness = OpenStartup(socket, process, TimeSpan.FromSeconds(60));

        Assert.Throws<TimeoutException>(harness.Coordinator.EnsureIdentified);

        Assert.Equal(1, process.LaunchCalls);
        Assert.Equal(1, socket.ConnectCalls);
        Assert.Empty(harness.Waits);
    }

    [Fact]
    public void EnsureIdentified_OwnedObsExitsDuringStartup_StopsRetrying()
    {
        var process = new FakeProcess { Exists = true, StartSucceeds = true };
        var socket = new FakeSession();
        socket.WhileNotIdentifying = _ => process.Owned = false;
        StartupHarness harness = OpenStartup(socket, process, TimeSpan.FromSeconds(60));

        Assert.Throws<TimeoutException>(harness.Coordinator.EnsureIdentified);

        Assert.Equal(1, socket.ConnectCalls);
        Assert.Equal(new[] { ObsCoordinator.LaunchSettle }, harness.Waits);
    }

    [Fact]
    public void Guard_RefusalNamesTheStreamingSettingAndTheArm()
    {
        Assert.Equal(TwitchIngestGuard.NotStartedMessage, TwitchIngestGuard.Refusal(true, true));
        Assert.Equal(TwitchIngestGuard.NotArmedMessage, TwitchIngestGuard.Refusal(true, false));
        Assert.Equal("OBS streaming is disabled.", TwitchIngestGuard.Refusal(false, true));
        Assert.Equal("OBS streaming is disabled.", TwitchIngestGuard.Refusal(false, false));
        Assert.Contains("obs arm", TwitchIngestGuard.NotArmedMessage, StringComparison.Ordinal);
        Assert.Equal(ObsStreamArm.NotArmedReason, TwitchIngestGuard.BlockedBy(true, false));
        Assert.Null(TwitchIngestGuard.BlockedBy(true, true));
        Assert.Null(TwitchIngestGuard.BlockedBy(false, false));
    }

    [Fact]
    public void Reconcile_NotArmed_DoesNotTouchObsAndReportsTheReason()
    {
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        int patches = 0;
        Harness harness = Open(
            Settings(),
            socket,
            beforeLaunch: () => patches++,
            armed: () => false
        );

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();
        ObsRuntimeSnapshot again = harness.Coordinator.ReconcileStream();

        Assert.Equal(0, patches);
        Assert.Equal(0, socket.ConnectCalls);
        Assert.Equal(0, socket.SelectionReads);
        Assert.Equal(0, socket.SelectCalls);
        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(0, harness.Process.LaunchCalls);
        Assert.True(snapshot.StreamDesired);
        Assert.False(snapshot.StreamActive);
        Assert.False(snapshot.Stream.Succeeded);
        Assert.Equal(ObsOutputFailure.NotRequested, snapshot.Stream.Failure);
        Assert.Equal(ObsStreamArm.NotArmedReason, snapshot.Stream.Reason);
        Assert.Equal("obs.stream_not_armed", snapshot.StreamBlockedBy);
        Assert.Equal(TwitchIngestGuard.NotArmedMessage, snapshot.Stream.Detail);
        Assert.Equal("obs.stream_not_armed", again.StreamBlockedBy);

        var status = new SpectatorStatus();
        ObsStatus.Copy(status, snapshot);
        Assert.Equal("obs.stream_not_armed", status.ObsStreamBlockedBy);
        Assert.Contains(
            "blocked=obs.stream_not_armed",
            ObsStatus.Describe(status),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Reconcile_ArmIsReadEachTime()
    {
        bool armed = false;
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
        };
        Harness harness = Open(Settings(), socket, armed: () => armed);

        ObsRuntimeSnapshot blocked = harness.Coordinator.ReconcileStream();
        armed = true;
        ObsRuntimeSnapshot started = harness.Coordinator.ReconcileStream();

        Assert.Equal(ObsStreamArm.NotArmedReason, blocked.StreamBlockedBy);
        Assert.True(started.Stream.Succeeded);
        Assert.True(started.StreamActive);
        Assert.Null(started.StreamBlockedBy);
        Assert.Equal(1, socket.StartStreamCalls);
    }

    [Fact]
    public void Reconcile_StreamingDisabled_IsNotReportedAsBlocked()
    {
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        Harness harness = Open(Settings(streaming: false), socket, armed: () => false);

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.False(snapshot.StreamDesired);
        Assert.Null(snapshot.StreamBlockedBy);
        Assert.Equal("OBS streaming is disabled.", snapshot.Stream.Detail);
    }

    [Fact]
    public void Reconcile_NoArmReader_FailsClosed()
    {
        var socket = new FakeSession { IsIdentified = true, IsConnected = true };
        var coordinator = new ObsCoordinator(
            NullLogger.Instance,
            new AppSettings { OBS = Settings() },
            socket,
            new FakeProcess(),
            new RecordingSession(NullLogger.Instance, socket, Fast(0)),
            _ => { },
            TimeSpan.FromMilliseconds(20)
        );

        ObsRuntimeSnapshot snapshot = coordinator.ReconcileStream();

        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(ObsStreamArm.NotArmedReason, snapshot.StreamBlockedBy);
    }

    [Theory]
    [InlineData("Untitled", "HeroesReplay", "obs.profile_mismatch")]
    [InlineData("HeroesReplay", "Untitled", "obs.collection_mismatch")]
    [InlineData("Untitled", "Untitled", "obs.profile_mismatch")]
    public void Reconcile_WrongProfileOrCollection_DoesNotChangeSceneOrStart(
        string profile,
        string collection,
        string reason
    )
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
            ProgramScene = "game-scene",
            ActiveProfile = profile,
            ActiveCollection = collection,
        };
        Harness harness = Open(Settings(), socket);

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.Equal(1, socket.SelectionReads);
        Assert.Equal(0, socket.SelectCalls);
        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal("game-scene", snapshot.SceneActual);
        Assert.False(snapshot.Stream.Succeeded);
        Assert.Equal(ObsOutputFailure.SelectionMismatch, snapshot.Stream.Failure);
        Assert.Equal(reason, snapshot.Stream.Reason);
        Assert.Equal(reason, snapshot.StreamBlockedBy);
        Assert.Contains("'Untitled'", snapshot.Stream.Detail, StringComparison.Ordinal);
        Assert.Contains("were not started", snapshot.Stream.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Reconcile_ConfiguredNames_AreTheOnesChecked()
    {
        OBSSettings obs = Settings();
        obs.ProfileName = "HeroesReplay-live";
        obs.SceneCollectionName = "HeroesReplay-live";
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
            ActiveProfile = "HeroesReplay-live",
            ActiveCollection = "HeroesReplay-live",
        };
        Harness harness = Open(obs, socket);

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.True(snapshot.Stream.Succeeded);
        Assert.Equal(1, socket.StartStreamCalls);
        Assert.Equal(
            "--profile \"HeroesReplay-live\" --collection \"HeroesReplay-live\"",
            ObsLaunchDecision
                .Decide(
                    true,
                    false,
                    "obs64.exe",
                    true,
                    ObsNames.Profile(obs),
                    ObsNames.SceneCollection(obs)
                )
                .Arguments
        );
    }

    [Fact]
    public void Reconcile_SelectionUnreadable_FailsClosed()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            ActivateOnStart = true,
            SelectionError = new InvalidOperationException("request failed"),
        };
        Harness harness = Open(Settings(), socket);

        ObsRuntimeSnapshot snapshot = harness.Coordinator.ReconcileStream();

        Assert.Equal(0, socket.SelectCalls);
        Assert.Equal(0, socket.StartStreamCalls);
        Assert.Equal(ObsOutputFailure.SelectionMismatch, snapshot.Stream.Failure);
        Assert.Equal(ObsSelection.Unreadable, snapshot.StreamBlockedBy);
        Assert.Contains("request failed", snapshot.Stream.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void StartRecording_WrongCollection_DoesNotStopOrStartARecording()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            Recording = true,
            ActiveCollection = "Untitled",
        };
        Harness harness = Open(Settings(streaming: false), socket, armed: () => false);

        ObsRecordingResult started = harness.Coordinator.StartRecording(() => true, 7, "unit");

        Assert.False(started.Succeeded);
        Assert.False(started.Owned);
        Assert.Equal(ObsOutputFailure.SelectionMismatch, started.Failure);
        Assert.Equal(ObsSelection.CollectionMismatch, started.Reason);
        Assert.Equal(0, socket.StopRecordCalls);
        Assert.Equal(0, socket.StartRecordCalls);
        Assert.Equal(1, socket.SelectionReads);

        var status = new SpectatorStatus();
        ObsStatus.CopyRecording(status, started);
        Assert.Equal(ObsSelection.CollectionMismatch, status.ObsRecordBlockedBy);
        Assert.Contains("'Untitled'", started.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void StartRecording_IsNotGatedByTheArm()
    {
        var socket = new FakeSession
        {
            IsIdentified = true,
            IsConnected = true,
            RecordOnStart = true,
        };
        Harness harness = Open(Settings(streaming: true), socket, armed: () => false);

        ObsRecordingResult started = harness.Coordinator.StartRecording(() => true, 7, "unit");

        Assert.True(started.Succeeded);
        Assert.True(started.Owned);
        Assert.Null(started.Reason);
        Assert.Equal(1, socket.StartRecordCalls);
        Assert.Equal(1, socket.SelectionReads);
        Assert.Equal(0, socket.StartStreamCalls);

        var status = new SpectatorStatus { ObsRecordBlockedBy = ObsSelection.ProfileMismatch };
        ObsStatus.CopyRecording(status, started);
        Assert.Null(status.ObsRecordBlockedBy);
    }

    [Fact]
    public void Startup_DesiredStateRequiresEnabledObsAndStreaming()
    {
        OBSSettings obs = Settings();
        Assert.True(ObsDesired.StreamIsDesired(obs));
        Assert.False(ObsDesired.StreamIsDesired(Settings(streaming: false)));
        Assert.False(ObsDesired.StreamIsDesired(Settings(enabled: false)));

        ObsRuntimeSnapshot snapshot = ObsDesired.Capture(
            obs,
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
        // ObsStatus only fills the object it is given; the caller's store writes status.json.
        // This test used to compare the live %LOCALAPPDATA%\HeroesReplay\status.json timestamp
        // before and after, which a running stack on the same machine rewrites every few
        // seconds (#331).
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
            int code = ServiceSupervisor
                .Stop(
                    path,
                    new ServiceShutdown
                    {
                        ProcessNameOrNull = _ => null,
                        Kill = _ => kills++,
                        RequestGracefulStop = () => { },
                        GracefulWait = TimeSpan.Zero,
                        Wait = _ => { },
                        ConfirmStream = plan =>
                        {
                            seen = plan;
                            Assert.True(plan.StopStream);
                            Assert.False(plan.CloseProcess);
                            return ObsStreamResult.ConfirmedInactive();
                        },
                    }
                )
                .ExitCode;

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
            Settings(streaming: streaming, closeOwned: closeOwned),
            socket,
            budget: Fast(0)
        );
    }

    private static Harness Open(
        OBSSettings obs,
        FakeSession socket = null,
        FakeProcess process = null,
        ObsRecordingBudget budget = null,
        Action beforeLaunch = null,
        Func<bool> armed = null,
        Func<ObsValidation> preflight = null,
        IObsMicrophoneSession microphones = null,
        SpectatorStatusStore statusStore = null,
        Action stateChanged = null,
        Func<DateTimeOffset> now = null,
        IObsIngestProbe ingest = null,
        IObsWindows windows = null,
        ILogger logger = null
    )
    {
        socket ??= new FakeSession();
        process ??= new FakeProcess();
        var waits = new List<TimeSpan>();
        ObsCoordinator coordinator = null;
        // Wired like ObsController: the writer reads the coordinator's newest state.
        if (statusStore != null)
        {
            stateChanged ??= () => ObsStatus.Write(statusStore, () => coordinator.State);
        }

        coordinator = new ObsCoordinator(
            logger ?? NullLogger.Instance,
            new AppSettings { OBS = obs },
            socket,
            process,
            new RecordingSession(NullLogger.Instance, socket, budget ?? Fast(0)),
            waits.Add,
            TimeSpan.FromMilliseconds(20),
            beforeLaunch,
            armed ?? (() => true),
            preflight,
            now: now,
            microphones: microphones,
            stateChanged: stateChanged,
            ingest: ingest,
            windows: windows
        );
        return new Harness
        {
            Coordinator = coordinator,
            Socket = socket,
            Process = process,
            Waits = waits,
        };
    }

    /// <summary>
    /// A coordinator on a fake clock: waits and each Connect that does not identify (its 10 s
    /// identify timeout) move it. Nothing sleeps, and no real OBS starts.
    /// </summary>
    private static StartupHarness OpenStartup(
        FakeSession socket,
        FakeProcess process,
        TimeSpan startup,
        ObsCrashSentinel sentinel = null
    )
    {
        var harness = new StartupHarness();
        DateTimeOffset start = new(2026, 10, 7, 14, 4, 56, TimeSpan.Zero);
        DateTimeOffset clock = start;
        socket.WhileNotIdentifying ??= timeout => clock += timeout;
        harness.Coordinator = new ObsCoordinator(
            NullLogger.Instance,
            new AppSettings { OBS = Settings(streaming: false) },
            socket,
            process,
            new RecordingSession(NullLogger.Instance, socket, Fast(0)),
            wait =>
            {
                harness.Waits.Add(wait);
                clock += wait;
            },
            TimeSpan.FromSeconds(10),
            startupIdentifyTimeout: startup,
            now: () => clock,
            sentinel: sentinel
        );
        harness.Since = () => clock - start;
        return harness;
    }

    private sealed class StartupHarness
    {
        public ObsCoordinator Coordinator { get; set; }
        public List<TimeSpan> Waits { get; } = new();
        public Func<TimeSpan> Since { get; set; }
        public TimeSpan Elapsed => Since();
    }

    private static OBSSettings Settings(
        string executable = null,
        bool enabled = true,
        bool streaming = true,
        string scene = WaitingScene,
        bool closeOwned = false,
        bool muteMicrophones = true
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
            MuteMicrophones = muteMicrophones,
        };

    private static ObsRecordingBudget Fast(int retryCount) =>
        new()
        {
            RetryCount = retryCount,
            RetryDelay = TimeSpan.Zero,
            StartTimeout = TimeSpan.FromMilliseconds(40),
            StreamStartTimeout = TimeSpan.FromMilliseconds(40),
            StopTimeout = TimeSpan.FromMilliseconds(40),
            PollInterval = TimeSpan.FromMilliseconds(5),
        };

    /// <summary>
    /// obs64 processes this test process started. Counting every obs64 on the machine made the
    /// test depend on whether someone opened or closed OBS meanwhile (#331).
    /// </summary>
    private static int ObsStartedByThisProcess() =>
        ProcessTable
            .Snapshot()
            .Count(entry =>
                entry.ParentPid == Environment.ProcessId
                && string.Equals(
                    entry.Name,
                    ObsLaunchDecision.ProcessName + ".exe",
                    StringComparison.OrdinalIgnoreCase
                )
            );

    private sealed class Harness
    {
        public ObsCoordinator Coordinator { get; init; }
        public FakeSession Socket { get; init; }
        public FakeProcess Process { get; init; }
        public List<TimeSpan> Waits { get; init; }
    }

    /// <summary>A TCP connect that never opens a socket: it records the target and answers <see cref="Failure"/>.</summary>
    private sealed class FakeIngest : IObsIngestProbe
    {
        public string Failure { get; set; }
        public List<string> Connects { get; } = new();

        public string Connect(string host, int port, TimeSpan timeout)
        {
            Connects.Add(host + ":" + port + " " + timeout.TotalSeconds + "s");
            return Failure;
        }
    }

    /// <summary>A desktop of fixed top-level windows. A close removes nothing; it is recorded.</summary>
    private sealed class FakeWindows : IObsWindows
    {
        public List<ObsTopWindow> Windows { get; } = new();
        public List<IntPtr> Closed { get; } = new();

        public void Add(int pid, string title, bool visible = true, bool hung = false) =>
            Windows.Add(
                new ObsTopWindow(new IntPtr(0x1000 + Windows.Count), pid, title, visible, hung)
            );

        public IReadOnlyList<ObsTopWindow> TopLevel() => Windows.ToList();

        public bool Close(IntPtr handle)
        {
            Closed.Add(handle);
            return true;
        }
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            lock (Lines)
            {
                Lines.Add((logLevel, formatter(state, exception)));
            }
        }
    }

    private sealed class FakeProcess : IObsProcess
    {
        public bool Running { get; set; }
        public bool Exists { get; set; }
        public bool Owned { get; set; }
        public int LaunchCalls { get; private set; }
        public int CloseCalls { get; private set; }

        /// <summary>A fake start that reports an owned, running OBS. No real process starts.</summary>
        public bool StartSucceeds { get; set; }

        public ObsLaunchDecision LastStart { get; private set; }

        /// <summary>Runs when the fake start is called, before it reports OBS running.</summary>
        public Action OnStart { get; set; }

        public bool IsOwned => Owned;

        public bool IsRunning() => Running;

        /// <summary>The OBS pid whose windows the dialog close may touch.</summary>
        public int? Pid { get; set; } = ObsPid;

        public int? ProcessId() => Pid;

        public bool ExecutableExists(string path) => Exists;

        public ObsLaunchDecision Start(ObsLaunchDecision decision)
        {
            LaunchCalls++;
            LastStart = decision;
            OnStart?.Invoke();
            if (StartSucceeds)
            {
                Running = true;
                Owned = true;
            }

            return decision with
            {
                Started = StartSucceeds,
            };
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
        public Exception SelectionError { get; set; }
        public bool RecordOnStart { get; set; }
        public string ActiveProfile { get; set; } = "HeroesReplay";
        public string ActiveCollection { get; set; } = "HeroesReplay";
        public int SelectionReads { get; private set; }
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

        /// <summary>When above zero, the Connect call with this number identifies.</summary>
        public int IdentifyOnConnectCall { get; set; }

        /// <summary>Runs on a Connect that does not identify, with its identify timeout (a fake clock).</summary>
        public Action<TimeSpan> WhileNotIdentifying { get; set; }

        public List<TimeSpan> ConnectTimeouts { get; } = new();

        public void Connect(string endpoint, string password, TimeSpan identifyTimeout)
        {
            ConnectCalls++;
            ConnectTimeouts.Add(identifyTimeout);
            LastEndpoint = endpoint;
            LastPassword = password;
            if (
                IdentifyOnConnect
                || (IdentifyOnConnectCall > 0 && ConnectCalls >= IdentifyOnConnectCall)
            )
            {
                IsIdentified = true;
                IsConnected = true;
                return;
            }

            WhileNotIdentifying?.Invoke(identifyTimeout);
        }

        public void Disconnect()
        {
            IsConnected = false;
            IsIdentified = false;
        }

        /// <summary>Runs inside SetCurrentProgramScene, as another thread would while OBS answers.</summary>
        public Action WhileSelecting { get; set; }

        public void SelectProgramScene(string sceneName)
        {
            SelectCalls++;
            Events.Add("scene:" + sceneName);
            WhileSelecting?.Invoke();
            if (SelectError != null)
            {
                throw SelectError;
            }

            ProgramScene = sceneName;
        }

        public string CurrentProfile()
        {
            SelectionReads++;
            if (SelectionError != null)
            {
                throw SelectionError;
            }

            return ActiveProfile;
        }

        public string CurrentSceneCollection() => ActiveCollection;

        /// <summary>GetStreamServiceSettings, without the key.</summary>
        public ObsStreamServer Server { get; set; } = new("rtmp_common", "Twitch", "auto");

        public ObsStreamServer StreamServer() => Server;

        public bool IsRecording() => Recording;

        public void StartRecord()
        {
            StartRecordCalls++;
            if (RecordOnStart)
            {
                Recording = true;
            }
        }

        public string StopRecord()
        {
            StopRecordCalls++;
            return null;
        }

        /// <summary>The first this many GetStreamStatus reads throw, like a request that timed out.</summary>
        public int StreamActiveErrors { get; set; }

        public bool IsStreamActive()
        {
            if (StreamActiveErrors > 0)
            {
                StreamActiveErrors--;
                throw new TimeoutException("GetStreamStatus timed out.");
            }

            return Streaming;
        }

        /// <summary>
        /// A started stream that cannot connect, as OBS reports it: StreamStateChanged starting,
        /// then stopped (and OBS opens its "Failed to connect" dialog).
        /// </summary>
        public bool RejectOnStart { get; set; }

        /// <summary>GetStreamStatus outputReconnecting. StopStream clears it, as OBS does.</summary>
        public bool Reconnecting { get; set; }

        /// <summary>A started stream reports reconnecting at once, as an ingest that refuses it.</summary>
        public bool ReconnectOnStart { get; set; }

        /// <summary>outputBytes. Each read of a live output adds to it unless <see cref="FreezeBytes"/>.</summary>
        public long Bytes { get; set; }

        public bool FreezeBytes { get; set; }

        /// <summary>GetStreamStatus throws this, like a websocket request that timed out.</summary>
        public Exception StreamReadError { get; set; }

        public ObsStreamSample ReadStream()
        {
            if (StreamReadError != null)
            {
                throw StreamReadError;
            }

            if (Streaming && !Reconnecting && !FreezeBytes)
            {
                Bytes += 750_000;
            }

            return new ObsStreamSample(Streaming, Streaming && Reconnecting, Bytes);
        }

        /// <summary>Runs inside StartStream, as another thread would while OBS answers.</summary>
        public Action WhileStarting { get; set; }

        public void StartStream()
        {
            StartStreamCalls++;
            Events.Add("start");
            WhileStarting?.Invoke();
            if (StartStreamError != null)
            {
                throw StartStreamError;
            }

            if (RejectOnStart)
            {
                Raise(ObsRecordSignal.Stream(ObsRecordSignalKind.StreamStarting));
                Raise(ObsRecordSignal.Stream(ObsRecordSignalKind.StreamStopped));
                return;
            }

            if (ActivateOnStart || (ActivateOnCall > 0 && StartStreamCalls >= ActivateOnCall))
            {
                Streaming = true;
                Reconnecting = ReconnectOnStart;
                FreezeBytes = false;
                Bytes = 0;
            }
        }

        public void StopStream()
        {
            StopStreamCalls++;
            Events.Add("stop");
            if (!KeepStreamingOnStop)
            {
                Streaming = false;
                Reconnecting = false;
            }
        }
    }
}
