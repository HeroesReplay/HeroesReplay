using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Status;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Connectivity;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ConnectivityWatchdogTests
{
    [Fact]
    public void Apply_DebouncesInternetLossAndRestore()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: false);
        fixture.Probe.Internet = false;

        Assert.False(fixture.Watchdog.Apply(FailSnapshot()));
        Assert.True(fixture.Watchdog.IsOnline);
        Assert.Equal(0, fixture.Obs.StartCalls);
        Assert.Equal(0, fixture.Obs.StopCalls);

        Assert.False(fixture.Watchdog.Apply(FailSnapshot()));
        Assert.True(fixture.Watchdog.Apply(FailSnapshot()));
        Assert.False(fixture.Watchdog.IsOnline);
        Assert.Equal(0, fixture.Obs.StopCalls);

        fixture.Probe.Internet = true;
        Assert.False(fixture.Watchdog.Apply(OkSnapshot()));
        Assert.False(fixture.Watchdog.IsOnline);
        Assert.True(fixture.Watchdog.Apply(OkSnapshot()));
        Assert.True(fixture.Watchdog.IsOnline);
        Assert.Equal(0, fixture.Obs.StartCalls);
    }

    [Fact]
    public void Apply_OfflineThenOnline_ArmsHeroesProfileRetryOnceWithoutStreaming()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: false);
        Assert.False(fixture.Resume.IsPending);

        fixture.Watchdog.Apply(FailSnapshot());
        fixture.Watchdog.Apply(FailSnapshot());
        Assert.False(fixture.Resume.IsPending);
        Assert.True(fixture.Watchdog.IsOnline);

        Assert.True(fixture.Watchdog.Apply(FailSnapshot()));
        Assert.False(fixture.Watchdog.IsOnline);
        Assert.False(fixture.Resume.IsPending);

        Assert.False(fixture.Watchdog.Apply(OkSnapshot()));
        Assert.False(fixture.Resume.IsPending);

        Assert.True(fixture.Watchdog.Apply(OkSnapshot()));
        Assert.True(fixture.Watchdog.IsOnline);
        Assert.True(fixture.Resume.IsPending);
        Assert.True(fixture.Resume.Consume());
        Assert.False(fixture.Resume.IsPending);
        Assert.False(fixture.Resume.Consume());
        Assert.Equal(0, fixture.Obs.StartCalls);
        Assert.Equal(0, fixture.Obs.StopCalls);
    }

    [Fact]
    public void Apply_DoesNotStartStreamWhenStreamingDisabled()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: false);
        DropThenRestore(fixture);
        Assert.Equal(0, fixture.Obs.StartCalls);
        Assert.Equal(0, fixture.Obs.StopCalls);
    }

    [Fact]
    public void Apply_RepairsStoppedStreamWithoutAnInternetEdge()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: true);
        Assert.False(fixture.Obs.IsStreaming());

        bool edge = fixture.Watchdog.Apply(OkSnapshot());

        Assert.False(edge);
        Assert.True(fixture.Watchdog.IsOnline);
        Assert.Equal(1, fixture.Obs.StartCalls);
        Assert.Equal(0, fixture.Obs.StopCalls);
        Assert.True(fixture.Obs.IsStreaming());

        Assert.False(fixture.Watchdog.Apply(OkSnapshot()));
        Assert.Equal(1, fixture.Obs.StartCalls);
        Assert.Equal(0, fixture.Obs.StopCalls);
    }

    [Fact]
    public void Apply_ActiveButReconnectingStream_GoesToTheReconcile()
    {
        // #395: the reconcile skipped any active output, so a reconnect stuck for 4 h 11 min on
        // 2026-10-09 was never repaired. The coordinator's reconcile decides what to do with it.
        using Fixture fixture = CreateFixture(streamingEnabled: true);
        fixture.Obs.Streaming = true;
        fixture.Obs.Health = Reconnecting();
        fixture.Obs.StartResult = ObsStreamResult.Waiting("OBS's own reconnect still has time.");

        fixture.Watchdog.Apply(OkSnapshot());

        Assert.Equal(1, fixture.Obs.StartCalls);
        Assert.Equal(0, fixture.Obs.StopCalls);
    }

    [Fact]
    public void Apply_LiveStream_IsLeftAlone()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: true);
        fixture.Obs.Streaming = true;

        fixture.Watchdog.Apply(OkSnapshot());

        Assert.Equal(0, fixture.Obs.StartCalls);
        Assert.Equal(0, fixture.Obs.StopCalls);
    }

    [Fact]
    public void Apply_StuckStreamWithStreamingOff_IsNeverTouched()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: false);
        fixture.Obs.Streaming = true;
        fixture.Obs.Health = Reconnecting();

        fixture.Watchdog.Apply(OkSnapshot());
        DropThenRestore(fixture);

        Assert.Equal(0, fixture.Obs.StartCalls);
        Assert.Equal(0, fixture.Obs.StopCalls);
    }

    [Fact]
    public void Apply_StuckStreamWhileOffline_IsLeftToObsOwnReconnect()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: true);
        fixture.Obs.Streaming = true;
        fixture.Obs.Health = Reconnecting();
        fixture.Obs.StartResult = ObsStreamResult.Waiting("OBS's own reconnect still has time.");
        fixture.Watchdog.Apply(FailSnapshot());
        fixture.Watchdog.Apply(FailSnapshot());
        int whileOnline = fixture.Obs.StartCalls;

        fixture.Watchdog.Apply(FailSnapshot());
        fixture.Watchdog.Apply(FailSnapshot());

        Assert.False(fixture.Watchdog.IsOnline);
        Assert.Equal(whileOnline, fixture.Obs.StartCalls);
        Assert.Equal(0, fixture.Obs.StopCalls);
    }

    [Fact]
    public void Apply_EveryTick_ReconcilesTheScene_LiveOrNotOnlineOrNot()
    {
        // #407: a live stream skipped the reconcile, so nothing put game-scene back after OBS
        // came back on waiting-screen and its stream went live.
        using Fixture fixture = CreateFixture(streamingEnabled: true);
        fixture.Obs.Streaming = true;

        fixture.Watchdog.Apply(OkSnapshot());
        fixture.Obs.Streaming = false;
        fixture.Watchdog.Apply(OkSnapshot());
        for (int i = 0; i < 4; i++)
        {
            fixture.Watchdog.Apply(FailSnapshot());
        }

        Assert.False(fixture.Watchdog.IsOnline);
        Assert.Equal(6, fixture.Obs.SceneReconciles);
        Assert.Equal(new[] { "scene", "start", "scene" }, fixture.Obs.Calls.Take(3));
    }

    [Fact]
    public void Apply_StreamingOff_NeverReconcilesTheScene()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: false);

        fixture.Watchdog.Apply(OkSnapshot());

        Assert.Equal(0, fixture.Obs.SceneReconciles);
    }

    private static ObsStreamHealth Reconnecting() =>
        ObsStreamHealth.Next(
            null,
            new ObsStreamSample(true, true, 199_373_948_174),
            DateTimeOffset.UtcNow.AddHours(-4)
        );

    [Fact]
    public void Apply_ShortOutageDoesNotStopTheStream()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: true);
        fixture.Obs.Streaming = true;

        DropThenRestore(fixture);

        Assert.Equal(0, fixture.Obs.StopCalls);
        Assert.Equal(0, fixture.Obs.StartCalls);
        Assert.True(fixture.Obs.IsStreaming());
        Assert.True(fixture.Resume.IsPending);
    }

    [Fact]
    public void Apply_CopiesDesiredVersusActualObsState()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: false);
        fixture.Obs.State = new ObsRuntimeSnapshot
        {
            ProcessRunning = true,
            WebsocketIdentified = true,
            SceneDesired = "waiting-screen",
            SceneActual = "game-scene",
            StreamDesired = true,
            StreamActive = false,
            Stream = ObsStreamResult.Failed(ObsOutputFailure.NotConfirmed, "not live"),
        };

        Assert.False(fixture.Watchdog.Apply(OkSnapshot()));

        var status = fixture.Store.Read();
        Assert.Equal(true, status.ObsProcessRunning);
        Assert.Equal(true, status.ObsWebsocketIdentified);
        Assert.Equal("waiting-screen", status.ObsSceneDesired);
        Assert.Equal("game-scene", status.ObsSceneActual);
        Assert.Equal(true, status.ObsStreamDesired);
        Assert.Equal(false, status.ObsStreamActive);
        Assert.Equal("not live", status.ObsDetail);
        Assert.Contains("stream desired=True", ObsStatus.Describe(status));
        Assert.Contains("active=False", ObsStatus.Describe(status));
        Assert.Equal(0, fixture.Obs.StartCalls);
    }

    [Fact]
    public void Apply_GameSceneMidSession_RewritesStatusJsonAndServicesStatusShowsIt()
    {
        // Production 2026-10-08 (#282): game-scene was on air 22 minutes into a replay while
        // status.json and `services status` still said waiting-screen.
        using Fixture fixture = CreateFixture(
            streamingEnabled: true,
            waitingScene: "waiting-screen"
        );
        fixture.Obs.Streaming = true;
        fixture.Obs.State = new ObsRuntimeSnapshot
        {
            ProcessRunning = true,
            WebsocketIdentified = true,
            SceneDesired = "waiting-screen",
            SceneActual = "waiting-screen",
            StreamDesired = true,
            StreamActive = true,
            Stream = ObsStreamResult.ConfirmedActive(),
        };
        fixture.Watchdog.Apply(OkSnapshot());
        SpectatorStatus waiting = new SpectatorStatusStore(fixture.Path).TryReadShared();
        Assert.Equal("waiting-screen", waiting.ObsSceneDesired);
        Assert.Equal("waiting-screen", waiting.ObsSceneActual);

        // Connectivity, its detail, and the stream block stay the same: only the scene changes.
        fixture.Obs.SwapToGameScene();
        fixture.Watchdog.Apply(OkSnapshot());

        SpectatorStatus onAir = new SpectatorStatusStore(fixture.Path).TryReadShared();
        Assert.Equal("game-scene", onAir.ObsSceneDesired);
        Assert.Equal("game-scene", onAir.ObsSceneActual);
        Assert.Equal(true, onAir.ObsStreamActive);
        Assert.Equal(0, fixture.Obs.StartCalls);

        var text = new StringWriter();
        ServiceSupervisor.Status(
            fixture.Path + ".services.json",
            pid => null,
            onAir,
            query: new ServiceStatusQuery { Out = text }
        );
        Assert.Contains(" scene=game-scene ", text.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("waiting-screen", text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_SameSceneDoesNotRewriteStatusJson()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: false);
        fixture.Obs.SwapToGameScene();
        fixture.Watchdog.Apply(OkSnapshot());
        Assert.True(File.Exists(fixture.Path));
        File.Delete(fixture.Path);

        fixture.Watchdog.Apply(OkSnapshot());

        Assert.False(File.Exists(fixture.Path));
    }

    [Fact]
    public void Apply_SceneTheSpectatorAlreadyWrote_IsNotWrittenAgain()
    {
        // #357: the scene switch reaches status.json from the OBS controller at once. The next
        // tick finds the same scene and the same connectivity there, so it writes nothing.
        using Fixture fixture = CreateFixture(streamingEnabled: false);
        fixture.Watchdog.Apply(OkSnapshot());
        fixture.Obs.SwapToGameScene();
        Assert.True(ObsStatus.Write(fixture.Store, fixture.Obs.ReadObsState));
        Assert.Equal("game-scene", fixture.Store.Read().ObsSceneActual);
        File.Delete(fixture.Path);

        fixture.Watchdog.Apply(OkSnapshot());

        Assert.False(File.Exists(fixture.Path));
        Assert.False(ObsStatus.Write(fixture.Store, fixture.Obs.ReadObsState));
    }

    [Fact]
    public async Task ProbeAsync_UsesInjectedProbe()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: true);
        fixture.Probe.Internet = true;
        fixture.Probe.Twitch = false;
        fixture.Probe.HeroesProfile = true;

        ConnectivitySnapshot snapshot = await fixture.Watchdog.ProbeAsync(CancellationToken.None);
        Assert.True(snapshot.Internet);
        Assert.True(snapshot.TwitchProbed);
        Assert.False(snapshot.Twitch);
        Assert.True(snapshot.HeroesProfile);
        Assert.False(snapshot.Healthy);
        Assert.Equal(1, fixture.Probe.TwitchCalls);
        Assert.Equal(0, fixture.Probe.HeroesProfileCalls);
    }

    [Fact]
    public async Task ProbeAsync_SkipsTwitchWebsiteUnlessStreamingEnabled()
    {
        using Fixture off = CreateFixture(streamingEnabled: false);
        ConnectivitySnapshot skipped = await off.Watchdog.ProbeAsync(CancellationToken.None);
        Assert.Equal(0, off.Probe.TwitchCalls);
        Assert.Equal(1, off.Probe.InternetCalls);
        Assert.Equal(0, off.Probe.HeroesProfileCalls);
        Assert.False(skipped.TwitchProbed);
        Assert.False(skipped.Twitch);
        Assert.True(skipped.Internet);
        Assert.True(skipped.HeroesProfile);
        Assert.True(skipped.Healthy);
        Assert.Contains("twitch=skipped", skipped.Describe(), StringComparison.Ordinal);

        using Fixture on = CreateFixture(streamingEnabled: true);
        on.Probe.Twitch = false;
        ConnectivitySnapshot probed = await on.Watchdog.ProbeAsync(CancellationToken.None);
        Assert.Equal(1, on.Probe.TwitchCalls);
        Assert.True(probed.TwitchProbed);
        Assert.False(probed.Twitch);
        Assert.False(probed.Healthy);
    }

    [Fact]
    public async Task RunAsync_DoesNotPollTwitchWhenStreamingDisabled()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: false);
        using var cts = new CancellationTokenSource();
        fixture.Probe.StopAtFirstProbe = cts;
        await fixture.Watchdog.RunAsync(cts.Token);
        Assert.Equal(0, fixture.Probe.TwitchCalls);
        Assert.Equal(1, fixture.Probe.InternetCalls);
        Assert.Equal(0, fixture.Probe.HeroesProfileCalls);
    }

    [Fact]
    public async Task RunAsync_ProbesTwitchWhenStreamingEnabled()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: true);
        using var cts = new CancellationTokenSource();
        fixture.Probe.StopAtFirstProbe = cts;
        await fixture.Watchdog.RunAsync(cts.Token);
        Assert.True(fixture.Probe.TwitchCalls >= 1);
        Assert.True(fixture.Probe.InternetCalls >= 1);
        Assert.Equal(0, fixture.Probe.HeroesProfileCalls);
    }

    [Fact]
    public async Task RunAsync_StreamingEnabled_ConfirmsStopOnCancelWithoutStarting()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: true);
        fixture.Obs.Streaming = true;
        using var cts = new CancellationTokenSource();
        fixture.Probe.StopAtFirstProbe = cts;

        await fixture.Watchdog.RunAsync(cts.Token);

        Assert.Equal(0, fixture.Obs.StartCalls);
        Assert.True(fixture.Obs.StopCalls >= 1);
        Assert.False(fixture.Obs.IsStreaming());
    }

    [Fact]
    public async Task RunAsync_ReleaseRestart_KeepsTheStreamLiveOnTheWaitingScene()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: true, waitingScene: "waiting");
        fixture.Obs.Streaming = true;
        fixture.Watchdog.KeepStreamThroughRestart(true);
        using var cts = new CancellationTokenSource();
        fixture.Probe.StopAtFirstProbe = cts;

        await fixture.Watchdog.RunAsync(cts.Token);

        Assert.Equal(0, fixture.Obs.StopCalls);
        Assert.True(fixture.Obs.IsStreaming());
        Assert.Equal(1, fixture.Obs.WaitingSceneCalls);
    }

    [Fact]
    public async Task RunAsync_ReleaseRestartWithoutAWaitingScene_StopsTheStream()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: true);
        fixture.Obs.Streaming = true;
        fixture.Watchdog.KeepStreamThroughRestart(true);
        using var cts = new CancellationTokenSource();
        fixture.Probe.StopAtFirstProbe = cts;

        await fixture.Watchdog.RunAsync(cts.Token);

        Assert.True(fixture.Obs.StopCalls >= 1);
        Assert.False(fixture.Obs.IsStreaming());
    }

    [Fact]
    public async Task RunAsync_ReleaseRestartCalledOff_StopsTheStreamOnAPlainStop()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: true, waitingScene: "waiting");
        fixture.Obs.Streaming = true;
        fixture.Watchdog.KeepStreamThroughRestart(true);
        fixture.Watchdog.KeepStreamThroughRestart(false);
        using var cts = new CancellationTokenSource();
        fixture.Probe.StopAtFirstProbe = cts;

        await fixture.Watchdog.RunAsync(cts.Token);

        Assert.True(fixture.Obs.StopCalls >= 1);
        Assert.Equal(0, fixture.Obs.WaitingSceneCalls);
    }

    [Fact]
    public void Apply_RestoreRequestsTheUnfinishedReplayWhenTheGameIsGone()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: false, gameRunning: false);
        string replayPath = Path.GetTempFileName();
        try
        {
            fixture.Store.Patch(status =>
            {
                status.ReplayId = 65268468;
                status.CompletedReplayId = 65268467;
                status.ReplayPath = replayPath;
            });
            DropThenRestore(fixture);
            Assert.True(fixture.Replays.TryTake(out int id, out string path));
            Assert.Equal(65268468, id);
            Assert.Equal(replayPath, path);
        }
        finally
        {
            File.Delete(replayPath);
        }
    }

    [Fact]
    public void Apply_RestoreLeavesARunningGameAlone()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: false, gameRunning: true);
        string replayPath = Path.GetTempFileName();
        try
        {
            fixture.Store.Patch(status =>
            {
                status.ReplayId = 65268468;
                status.ReplayPath = replayPath;
            });
            DropThenRestore(fixture);
            Assert.False(fixture.Replays.TryTake(out _, out _));
        }
        finally
        {
            File.Delete(replayPath);
        }
    }

    [Fact]
    public void Apply_WritesTheStreamBlockReasonWhenItChanges()
    {
        using Fixture fixture = CreateFixture(streamingEnabled: true);
        fixture.Obs.StartResult = ObsStreamResult.NotArmed();
        fixture.Obs.State = new ObsRuntimeSnapshot
        {
            StreamDesired = true,
            Stream = ObsStreamResult.NotArmed(),
            StreamBlockedBy = ObsStreamArm.NotArmedReason,
        };

        fixture.Watchdog.Apply(OkSnapshot());

        Assert.Equal(1, fixture.Obs.StartCalls);
        Assert.False(fixture.Obs.IsStreaming());
        SpectatorStatus blocked = fixture.Store.Read();
        Assert.Equal("obs.stream_not_armed", blocked.ObsStreamBlockedBy);
        Assert.Equal(TwitchIngestGuard.NotArmedMessage, blocked.ObsDetail);

        // Same connectivity: only the cleared reason makes the watchdog write again.
        fixture.Obs.State = new ObsRuntimeSnapshot
        {
            StreamDesired = true,
            StreamActive = true,
            Stream = ObsStreamResult.ConfirmedActive(),
        };
        fixture.Watchdog.Apply(OkSnapshot());

        SpectatorStatus cleared = fixture.Store.Read();
        Assert.Null(cleared.ObsStreamBlockedBy);
        Assert.Equal(true, cleared.ObsStreamActive);
    }

    [Fact]
    public void AppSettings_StreamingEnabledIsFalse()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        Assert.True(File.Exists(path), $"expected {path} to be copied to the test output.");
        string json = File.ReadAllText(path);
        Assert.Contains("\"StreamingEnabled\": false", json, StringComparison.Ordinal);
    }

    private static void DropThenRestore(Fixture fixture)
    {
        fixture.Watchdog.Apply(FailSnapshot());
        fixture.Watchdog.Apply(FailSnapshot());
        fixture.Watchdog.Apply(FailSnapshot());
        Assert.False(fixture.Watchdog.IsOnline);
        fixture.Watchdog.Apply(OkSnapshot());
        fixture.Watchdog.Apply(OkSnapshot());
        Assert.True(fixture.Watchdog.IsOnline);
    }

    private static ConnectivitySnapshot FailSnapshot() =>
        new()
        {
            Internet = false,
            Twitch = false,
            HeroesProfile = false,
        };

    private static ConnectivitySnapshot OkSnapshot() =>
        new()
        {
            Internet = true,
            Twitch = true,
            HeroesProfile = true,
        };

    private static Fixture CreateFixture(
        bool streamingEnabled,
        bool gameRunning = true,
        string waitingScene = null
    )
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"heroesreplay-connectivity-{Guid.NewGuid():N}.json"
        );
        string resumePath = Path.Combine(
            Path.GetTempPath(),
            $"heroesreplay-replay-resume-{Guid.NewGuid():N}.json"
        );
        var store = new SpectatorStatusStore(path);
        var probe = new FakeProbe();
        var obs = new FakeObs();
        var resume = new HeroesProfileResume();
        var replays = new ReplayResumeFile(resumePath);
        var watchdog = new ConnectivityWatchdog(
            NullLogger<ConnectivityWatchdog>.Instance,
            new AppSettings
            {
                OBS = new OBSSettings
                {
                    StreamingEnabled = streamingEnabled,
                    WaitingSceneName = waitingScene,
                },
                Connectivity = new ConnectivitySettings
                {
                    FailThreshold = 3,
                    RecoverThreshold = 2,
                    Interval = TimeSpan.FromMilliseconds(1),
                    IdleInterval = TimeSpan.FromMinutes(5),
                },
            },
            probe,
            store,
            new CancellationTokenProvider(),
            obs,
            resume,
            replays,
            () => gameRunning
        );
        return new Fixture(path, resumePath, probe, obs, watchdog, resume, store, replays);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(
            string path,
            string resumePath,
            FakeProbe probe,
            FakeObs obs,
            ConnectivityWatchdog watchdog,
            HeroesProfileResume resume,
            SpectatorStatusStore store,
            ReplayResumeFile replays
        )
        {
            Path = path;
            ResumePath = resumePath;
            Probe = probe;
            Obs = obs;
            Watchdog = watchdog;
            Resume = resume;
            Store = store;
            Replays = replays;
        }

        public string Path { get; }
        public string ResumePath { get; }
        public FakeProbe Probe { get; }
        public FakeObs Obs { get; }
        public ConnectivityWatchdog Watchdog { get; }
        public HeroesProfileResume Resume { get; }
        public SpectatorStatusStore Store { get; }
        public ReplayResumeFile Replays { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }

            if (File.Exists(ResumePath))
            {
                File.Delete(ResumePath);
            }
        }
    }

    private sealed class FakeProbe : INetworkProbe
    {
        public bool Internet { get; set; } = true;
        public bool Twitch { get; set; } = true;
        public bool HeroesProfile { get; set; } = true;
        public int InternetCalls { get; private set; }
        public int TwitchCalls { get; private set; }
        public int HeroesProfileCalls { get; private set; }

        /// <summary>
        /// Cancelled at the first internet probe, so <c>RunAsync</c> finishes that round and then
        /// stops. A token that cancelled itself after 200 ms could fire before a busy machine
        /// ran the first probe at all (#331).
        /// </summary>
        public CancellationTokenSource StopAtFirstProbe { get; set; }

        public Task<bool> ProbeInternetAsync(CancellationToken cancellationToken)
        {
            InternetCalls++;
            StopAtFirstProbe?.Cancel();
            return Task.FromResult(Internet);
        }

        public Task<bool> ProbeTwitchAsync(CancellationToken cancellationToken)
        {
            TwitchCalls++;
            return Task.FromResult(Twitch);
        }

        public Task<bool> ProbeHeroesProfileAsync(CancellationToken cancellationToken)
        {
            HeroesProfileCalls++;
            return Task.FromResult(HeroesProfile);
        }
    }

    private sealed class FakeObs : IObsController
    {
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public bool Streaming { get; set; }
        public ObsRuntimeSnapshot State { get; set; }
        public ObsStreamResult StartResult { get; set; }

        public void BeginSession() { }

        public void EndSession() { }

        public void ConfigureFromContext() { }

        public Task CycleReportAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        /// <summary>Like <see cref="ObsController"/>: an accepted scene is the desired and the actual one.</summary>
        public void SwapToGameScene() =>
            State = (State ?? new ObsRuntimeSnapshot()) with
            {
                SceneDesired = "game-scene",
                SceneActual = "game-scene",
            };

        public void UpdateReplayInfoVisibility(TimeSpan matchTime) { }

        public int WaitingSceneCalls { get; private set; }

        public void SwapToWaitingScene() => WaitingSceneCalls++;

        public ObsRecordingResult StartRecording() =>
            ObsRecordingResult.Failed(ObsOutputFailure.NotRequested, "test");

        public ObsRecordingResult StopRecording() =>
            ObsRecordingResult.Failed(ObsOutputFailure.NotRequested, "test");

        /// <summary>The stream and scene calls in order: <c>start</c> and <c>scene</c>.</summary>
        public List<string> Calls { get; } = new();

        public int SceneReconciles { get; private set; }

        public void ReconcileScene()
        {
            SceneReconciles++;
            Calls.Add("scene");
        }

        public ObsStreamResult StartStreaming()
        {
            StartCalls++;
            Calls.Add("start");
            if (StartResult != null)
            {
                return StartResult;
            }

            Streaming = true;
            return ObsStreamResult.ConfirmedActive();
        }

        public ObsStreamResult StopStreaming()
        {
            StopCalls++;
            Streaming = false;
            return ObsStreamResult.ConfirmedInactive();
        }

        /// <summary>When set, what <see cref="ReadStreamHealth"/> reports, whatever <see cref="Streaming"/> says.</summary>
        public ObsStreamHealth Health { get; set; }

        public ObsStreamHealth ReadStreamHealth() =>
            Health
            ?? ObsStreamHealth.Next(
                null,
                new ObsStreamSample(Streaming, false, 1000),
                DateTimeOffset.UtcNow
            );

        public bool IsStreaming() => Streaming;

        public ObsRuntimeSnapshot ReadObsState() => State;
    }
}
