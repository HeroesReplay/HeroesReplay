using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Replays.Context;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating;
using HeroesReplay.Core.Spectating.Control;
using HeroesReplay.Core.Spectating.Session;
using HeroesReplay.Core.Status;
using HeroesReplay.Core.YouTube.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.Spectating.Session;

/// <summary>
/// The end of a verified session: the outcome is heard before the report scenes, and a
/// services stop during the report ends the wait without launching the next replay.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class GameManagerReportTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StopDuringTheReport_OutcomeCameFirstAndTheNextReplayIsNotLaunched()
    {
        using var fixture = new Fixture();
        var heard = new List<ReplaySessionKind>();
        fixture.Obs.Heard = heard;

        Task<ReplaySessionKind> session = fixture.Manager.FinishSessionAsync(
            new LoadedReplay { ReplayId = 101 },
            enteredMatch: true,
            obsSession: true,
            heard.Add,
            () => Task.FromResult(fixture.Next)
        );
        await fixture.Obs.Reporting.Task.WaitAsync(Patience);

        Assert.Equal(new[] { ReplaySessionKind.Played }, fixture.Obs.HeardWhenReportStarted);
        Assert.False(session.IsCompleted);

        // services stop: the 8 s settle and the 90 s hold must not be waited out.
        fixture.Stop.Cancel();
        ReplaySessionKind kind = await session.WaitAsync(Patience);

        Assert.Equal(ReplaySessionKind.Played, kind);
        Assert.Equal(new[] { ReplaySessionKind.Played }, heard);
        Assert.Equal(0, fixture.Game.Launches);
        Assert.Equal(1, fixture.Obs.EndSessions);
    }

    [Fact]
    public async Task StopBeforeTheReport_HearsTheOutcomeAndSkipsTheReport()
    {
        using var fixture = new Fixture();
        var heard = new List<ReplaySessionKind>();
        bool nextLoaded = false;
        fixture.Stop.Cancel();

        ReplaySessionKind kind = await fixture
            .Manager.FinishSessionAsync(
                new LoadedReplay { ReplayId = 101 },
                enteredMatch: true,
                obsSession: true,
                heard.Add,
                () =>
                {
                    nextLoaded = true;
                    return Task.FromResult(fixture.Next);
                }
            )
            .WaitAsync(Patience);

        Assert.Equal(ReplaySessionKind.Played, kind);
        Assert.Equal(new[] { ReplaySessionKind.Played }, heard);
        Assert.False(nextLoaded);
        Assert.False(fixture.Obs.Reporting.Task.IsCompleted);
        Assert.Equal(0, fixture.Game.Launches);
        Assert.Equal(1, fixture.Obs.EndSessions);
    }

    /// <summary>A staged release loads no next replay. The report is not cut, then the waiting scene stays.</summary>
    [Fact]
    public async Task NoNextReplay_TheReportPlaysOutThenTheWaitingSceneStays()
    {
        using var fixture = new Fixture();

        Task<ReplaySessionKind> session = fixture.Manager.FinishSessionAsync(
            new LoadedReplay { ReplayId = 101 },
            enteredMatch: true,
            obsSession: true,
            _ => { },
            () => Task.FromResult<LoadedReplay>(null)
        );
        await fixture.Obs.Reporting.Task.WaitAsync(Patience);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.False(session.IsCompleted);
        Assert.Equal(0, fixture.Obs.WaitingScenes);

        fixture.Obs.ReportEnds.TrySetResult();

        Assert.Equal(ReplaySessionKind.Played, await session.WaitAsync(Patience));
        Assert.Equal(1, fixture.Obs.WaitingScenes);
        Assert.Equal(0, fixture.Game.Launches);
        Assert.Equal(1, fixture.Obs.EndSessions);
    }

    /// <summary>
    /// OBS that does not identify (still starting, or stuck on its Safe Mode prompt) is a
    /// warning. The replay is not abandoned: the session goes on to spectate without OBS.
    /// </summary>
    [Fact]
    public void ObsThatDoesNotIdentify_DoesNotEndTheSession()
    {
        using var fixture = new Fixture();
        fixture.Obs.BeginError = new TimeoutException(
            "OBS websocket at ws://127.0.0.1:4455 did not identify in time."
        );

        bool identified = fixture.Manager.BeginObsSession(new LoadedReplay { ReplayId = 101 });

        Assert.False(identified);
        Assert.Equal(1, fixture.Obs.BeginSessions);
        Assert.False(fixture.Status.Read().ObsSession);
    }

    [Fact]
    public void ObsThatIdentifies_StartsTheObsSession()
    {
        using var fixture = new Fixture();

        bool identified = fixture.Manager.BeginObsSession(new LoadedReplay { ReplayId = 101 });

        Assert.True(identified);
        Assert.True(fixture.Status.Read().ObsSession);
    }

    /// <summary>Without an OBS session the outcome is still heard and no report scene is tried.</summary>
    [Fact]
    public async Task SessionWithoutObs_HearsTheOutcomeAndSkipsTheReport()
    {
        using var fixture = new Fixture();
        var heard = new List<ReplaySessionKind>();

        ReplaySessionKind kind = await fixture
            .Manager.FinishSessionAsync(
                new LoadedReplay { ReplayId = 101 },
                enteredMatch: true,
                obsSession: false,
                heard.Add,
                () => Task.FromResult(fixture.Next)
            )
            .WaitAsync(Patience);

        Assert.Equal(ReplaySessionKind.Played, kind);
        Assert.Equal(new[] { ReplaySessionKind.Played }, heard);
        Assert.False(fixture.Obs.Reporting.Task.IsCompleted);
        Assert.Equal(0, fixture.Obs.EndSessions);
    }

    /// <summary>
    /// #318: the stream reconcile can reach OBS after BeginSession failed, and the recording then
    /// starts. The session end stops it even though the replay had no OBS session.
    /// </summary>
    [Fact]
    public void SessionEnd_WithoutAnObsSession_StopsTheRecordingItOwns()
    {
        using var fixture = new Fixture();
        string file = Path.Combine(Path.GetTempPath(), "65820711", "owned.mp4");
        fixture.Obs.StopResult = ObsRecordingResult.FinalizedAt(file);

        ObsRecordingResult stopped = fixture.Manager.StopSessionRecording(
            new LoadedReplay { ReplayId = 65820711 },
            obsSession: false
        );

        Assert.Equal(1, fixture.Obs.StopRecordings);
        Assert.True(stopped.Finalized);
        Assert.Equal(file, stopped.OutputPath);
        Assert.Equal(0, fixture.Obs.EndSessions);
    }

    /// <summary>
    /// With nothing owned, ObsController answers NotOwned without sending OBS anything. Without an
    /// OBS session the session end then has no recording result, as before.
    /// </summary>
    [Fact]
    public void SessionEnd_WithoutAnObsSessionOrARecording_HasNoResult()
    {
        using var fixture = new Fixture();
        fixture.Obs.StopResult = ObsRecordingResult.Failed(
            ObsOutputFailure.NotOwned,
            "This process does not own the OBS recording."
        );

        Assert.Null(
            fixture.Manager.StopSessionRecording(
                new LoadedReplay { ReplayId = 65820711 },
                obsSession: false
            )
        );
        Assert.Equal(1, fixture.Obs.StopRecordings);
    }

    /// <summary>A graceful services stop mid-match stops the recording and leaves the stream.</summary>
    [Fact]
    public void ServicesStop_StopsTheOwnedRecordingAndNeverTheStream()
    {
        using var fixture = new Fixture();
        string file = Path.Combine(Path.GetTempPath(), "65820711", "owned.mp4");
        fixture.Obs.StopResult = ObsRecordingResult.FinalizedAt(file);
        fixture.Stop.Cancel();

        ObsRecordingResult stopped = fixture.Manager.StopSessionRecording(
            new LoadedReplay { ReplayId = 65820711 },
            obsSession: true
        );

        Assert.True(stopped.Finalized);
        Assert.Equal(1, fixture.Obs.StopRecordings);
    }

    [Fact]
    public void ServicesStop_AFailedStopIsStillTheSessionsResult()
    {
        using var fixture = new Fixture();
        fixture.Obs.StopResult = ObsRecordingResult.Failed(
            ObsOutputFailure.Timeout,
            "Timed out waiting for the finalized OBS recording path."
        );
        fixture.Stop.Cancel();

        ObsRecordingResult stopped = fixture.Manager.StopSessionRecording(
            new LoadedReplay { ReplayId = 65820711 },
            obsSession: true
        );

        Assert.Equal(ObsOutputFailure.Timeout, stopped.Failure);
        Assert.Equal(1, fixture.Obs.StopRecordings);
    }

    [Fact]
    public async Task StopDuringTheHold_EndsItWithoutLaunching()
    {
        using var fixture = new Fixture();
        var report = new TaskCompletionSource();

        Task<bool> hold = fixture.Manager.HoldBeforeNextLaunchAsync(
            report.Task,
            TimeSpan.FromSeconds(90),
            fixture.Stop.Token
        );
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.False(hold.IsCompleted);

        fixture.Stop.Cancel();

        Assert.False(await hold.WaitAsync(Patience));
    }

    [Fact]
    public async Task HoldThatRunsOut_AfterTheReport_LetsTheNextReplayLaunch()
    {
        using var fixture = new Fixture();

        bool launch = await fixture
            .Manager.HoldBeforeNextLaunchAsync(
                Task.CompletedTask,
                TimeSpan.FromMilliseconds(50),
                fixture.Stop.Token
            )
            .WaitAsync(Patience);

        Assert.True(launch);
        Assert.Equal(1, fixture.Obs.WaitingScenes);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            "hr-report-" + Path.GetRandomFileName()
        );

        public Fixture()
        {
            Directory.CreateDirectory(root);
            string nextPath = Path.Combine(root, "202.StormReplay");
            File.WriteAllText(nextPath, "next");
            Next = new LoadedReplay { ReplayId = 202, FileInfo = new FileInfo(nextPath) };
            Status = new SpectatorStatusStore(Path.Combine(root, "status.json"));
            var settings = new AppSettings
            {
                Location = new LocationSettings { DataDirectory = root },
                OBS = new OBSSettings
                {
                    Enabled = true,
                    WaitingSceneName = "Waiting",
                    BeforeNextReplay = TimeSpan.FromSeconds(90),
                },
            };
            Manager = new GameManager(
                settings,
                new NoContextSetter(),
                new VerifiedSpectator(),
                Game,
                Obs,
                new NoContext(),
                Status,
                new StormClientConfigurator(settings),
                new NotOnYouTube(),
                new RecordingClock(),
                NullLogger<GameManager>.Instance,
                new MediaPolicyAttemptLog(Path.Combine(root, "attempts"), logger: null),
                new NoGameData(),
                new CancellationTokenProvider(Stop.Token)
            );
        }

        public CancellationTokenSource Stop { get; } = new();

        public ClosedGame Game { get; } = new();

        public ReportObs Obs { get; } = new();

        public LoadedReplay Next { get; }

        public SpectatorStatusStore Status { get; }

        public GameManager Manager { get; }

        public void Dispose()
        {
            Stop.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class ReportObs : IObsController
    {
        public List<ReplaySessionKind> Heard { get; set; } = new();

        public ReplaySessionKind[] HeardWhenReportStarted { get; private set; }

        public TaskCompletionSource Reporting { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int EndSessions { get; private set; }

        public int WaitingScenes { get; private set; }

        /// <summary>Completing it ends the report cycle on its own.</summary>
        public TaskCompletionSource ReportEnds { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Like ObsController, a cancelled report ends without an error.</summary>
        public async Task CycleReportAsync(CancellationToken cancellationToken = default)
        {
            HeardWhenReportStarted = Heard.ToArray();
            Reporting.TrySetResult();
            try
            {
                await ReportEnds.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>BeginSession throws this, like an OBS that does not identify.</summary>
        public Exception BeginError { get; set; }

        public int BeginSessions { get; private set; }

        public void BeginSession()
        {
            BeginSessions++;
            if (BeginError != null)
            {
                throw BeginError;
            }
        }

        public void EndSession() => EndSessions++;

        public void ConfigureFromContext() { }

        public void SwapToGameScene() { }

        public void UpdateReplayInfoVisibility(TimeSpan matchTime) { }

        public void SwapToWaitingScene() => WaitingScenes++;

        public ObsRecordingResult StartRecording() =>
            ObsRecordingResult.Failed(ObsOutputFailure.NotRequested, "test");

        /// <summary>What StopRecording returns. NotOwned is ObsController with nothing recording.</summary>
        public ObsRecordingResult StopResult { get; set; } =
            ObsRecordingResult.Failed(ObsOutputFailure.NotRequested, "test");

        public int StopRecordings { get; private set; }

        public ObsRecordingResult StopRecording()
        {
            StopRecordings++;
            return StopResult;
        }

        /// <summary>Throws: the session end never starts or stops the stream.</summary>
        public ObsStreamResult StartStreaming() => throw new NotSupportedException();

        public ObsStreamResult StopStreaming() => throw new NotSupportedException();

        public ObsStreamHealth ReadStreamHealth() =>
            ObsStreamHealth.Unknown(null, "test", DateTimeOffset.UtcNow);

        public ObsRuntimeSnapshot ReadObsState() => throw new NotSupportedException();
    }

    /// <summary>The finished match's client is already closed.</summary>
    private sealed class ClosedGame : IGameController
    {
        public int Launches { get; private set; }

        public Task<bool> StartAuthenticatedReplayAsync(
            string replayPath,
            string replayVersion = null
        )
        {
            Launches++;
            return Task.FromResult(true);
        }

        public bool IsGameRunning() => false;

        public Task<ClientHoldReason> LaunchAsync() => throw new NotSupportedException();

        public Task<bool> OpenReplayFromHomeScreenAsync(string replayPath) =>
            Task.FromResult(false);

        public Task<TimeSpan?> TryReadRunningMatchClockAsync() => Task.FromResult<TimeSpan?>(null);

        public Task<bool> IsReplayPresentedAsync(LoadedReplay replay) => Task.FromResult(false);

        public Task<bool> TrySeeEndScreenAsync(bool nearCore) => throw new NotSupportedException();

        public void SendFocus(int player) => throw new NotSupportedException();

        public void SendPanel(Panel panel) => throw new NotSupportedException();

        public void ShowSelectedUnit() => throw new NotSupportedException();

        public void SaveEndScreenshot() { }

        public bool IsGameHung() => false;

        public bool ReplayFileOpened => false;

        public Process GetGameProcess() => null;

        public void Kill() { }
    }

    private sealed class VerifiedSpectator : ISpectator
    {
        public bool MatchClockSeen => true;

        public MatchOutcome Outcome => MatchOutcome.VerifiedCompleted;

        public void RecordHold(ClientHoldReason hold) => throw new NotSupportedException();

        public Task SpectateAsync() => throw new NotSupportedException();
    }

    private sealed class NoContextSetter : IReplayContextSetter
    {
        public Task SetContextAsync(LoadedReplay stormReplay) => Task.CompletedTask;
    }

    private sealed class NoContext : IReplayContext
    {
        public ContextData Previous => null;

        public ContextData Current => null;
    }

    private sealed class NotOnYouTube : IYouTubeReplayLookup
    {
        public Task<bool> AlreadyUploadedAsync(
            LoadedReplay replay,
            CancellationToken cancellationToken
        ) => Task.FromResult(false);
    }

    private sealed class NoGameData : IGameData
    {
        public IReadOnlyDictionary<string, UnitGroup> UnitGroups { get; } =
            new Dictionary<string, UnitGroup>();

        public IReadOnlyList<Hero> Heroes { get; } = Array.Empty<Hero>();

        public IReadOnlyCollection<string> CoreUnits { get; } = Array.Empty<string>();

        public IReadOnlyCollection<string> BossUnits { get; } = Array.Empty<string>();

        public IReadOnlyCollection<string> VehicleUnits { get; } = Array.Empty<string>();

        public IReadOnlyList<Map> Maps { get; } = Array.Empty<Map>();

        public UnitGroup GetUnitGroup(string unitName) => default;

        public Task LoadDataAsync() => Task.CompletedTask;
    }
}
