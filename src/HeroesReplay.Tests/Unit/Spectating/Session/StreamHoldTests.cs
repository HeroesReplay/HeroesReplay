using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating;
using HeroesReplay.Core.Spectating.Session;
using HeroesReplay.Core.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.Spectating.Session;

/// <summary>
/// #396: on 2026-10-09 spectate played replay after replay into a stream that was off air for
/// 4 h 11 min. Between replays, a desired stream that is not live holds the next replay until it
/// is live again, with no time limit.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class StreamHoldTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 5, 51, 48, TimeSpan.Zero);
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-stream-hold-" + Guid.NewGuid().ToString("N")
    );

    public StreamHoldTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(ObsStreamState.Inactive)]
    [InlineData(ObsStreamState.Reconnecting)]
    [InlineData(ObsStreamState.Stalled)]
    [InlineData(ObsStreamState.Unknown)]
    public void ADesiredStreamThatIsNotLive_HoldsTheNextReplay(ObsStreamState state)
    {
        Rig rig = Build();
        rig.Obs.Now = Health(state);

        StreamHoldCheck check = rig.Hold.Check();

        Assert.True(check.Holds);
        Assert.Equal(state, check.Health.State);
        Assert.StartsWith(state.ToString(), check.Reason, StringComparison.Ordinal);
        Assert.Equal(1, rig.Obs.Checks);
    }

    [Fact]
    public void ALiveStream_DoesNotHold()
    {
        Rig rig = Build();
        rig.Obs.Now = Health(ObsStreamState.Live);

        StreamHoldCheck check = rig.Hold.Check();

        Assert.False(check.Holds);
        Assert.True(check.Health.IsLive);
    }

    [Theory]
    [InlineData("dev settings")]
    [InlineData("obs disabled")]
    [InlineData("not armed")]
    [InlineData("setting off")]
    [InlineData("offline")]
    public void ABoxThatDoesNotStream_NeverHoldsAndDoesNotAskObs(string why)
    {
        Rig rig = Build(
            streaming: why != "dev settings",
            obsEnabled: why != "obs disabled",
            armed: why != "not armed",
            holdSetting: why != "setting off",
            online: why != "offline"
        );
        rig.Obs.Now = Health(ObsStreamState.Inactive);

        StreamHoldCheck check = rig.Hold.Check();

        Assert.False(check.Holds);
        Assert.Equal(0, rig.Obs.Checks);
    }

    [Fact]
    public void AMatchOnScreen_IsNeverHeld()
    {
        // The report already launched the next replay, so its match is on screen.
        Rig rig = Build(clientRunning: true);
        rig.Obs.Now = Health(ObsStreamState.Reconnecting);

        StreamHoldCheck check = rig.Hold.Check();
        StreamHoldCheck afterTheMatch = rig.Hold.Check(matchOver: true);

        Assert.False(check.Holds);
        Assert.Contains("match on screen", check.Reason, StringComparison.Ordinal);
        Assert.True(afterTheMatch.Holds);
    }

    [Fact]
    public async Task TheHold_EndsWhenTheStreamIsLive_AndShowsStreamHoldMeanwhile()
    {
        Rig rig = Build();
        rig.Obs.Script(
            ObsStreamState.Reconnecting,
            ObsStreamState.Reconnecting,
            ObsStreamState.Inactive,
            ObsStreamState.Live
        );
        SpectatorStatus during = null;
        rig.OnDelay = () => during ??= rig.Status.Read();

        StreamHoldEnd end = await rig.Hold.HoldAsync(
            rig.Hold.Check(),
            null,
            CancellationToken.None
        );

        Assert.Equal(StreamHoldEnd.Live, end);
        Assert.Equal(StreamHold.Phase, during.Phase);
        Assert.True(during.SpectatorRunning);
        Assert.Equal(T0, during.StreamHoldSince);
        Assert.StartsWith("Reconnecting.", during.StreamHoldReason, StringComparison.Ordinal);
        SpectatorStatus after = rig.Status.Read();
        Assert.NotEqual(StreamHold.Phase, after.Phase);
        Assert.Null(after.StreamHoldSince);
        Assert.Null(after.StreamHoldReason);
        Assert.Equal(1, rig.Obs.WaitingScenes);
        Assert.Equal((T0, true), (rig.Holds.First().Since.Value, rig.Holds.First().Reason != null));
        Assert.Null(rig.Holds.Last().Since);
        Assert.Single(rig.Log.Where(line => line.Level == LogLevel.Warning));
        Assert.Contains(
            rig.Log,
            line => line.Level == LogLevel.Information && line.Text.Contains("ends after")
        );
    }

    [Fact]
    public async Task ALongHold_HasNoTimeLimit_AndLogsEveryFiveMinutes()
    {
        Rig rig = Build();
        rig.Obs.Now = Health(ObsStreamState.Inactive);
        rig.OnDelay = () =>
        {
            if (rig.Clock - T0 >= TimeSpan.FromHours(1))
            {
                rig.Obs.Now = Health(ObsStreamState.Live);
            }
        };

        StreamHoldEnd end = await rig.Hold.HoldAsync(
            rig.Hold.Check(),
            null,
            CancellationToken.None
        );

        Assert.Equal(StreamHoldEnd.Live, end);
        Assert.Single(rig.Log.Where(line => line.Level == LogLevel.Warning));
        Assert.Equal(
            11,
            rig.Log.Count(line =>
                line.Level == LogLevel.Information && line.Text.StartsWith("Still holding")
            )
        );
    }

    [Fact]
    public async Task AStagedRelease_EndsTheHoldSoItCanInstall()
    {
        Rig rig = Build();
        rig.Obs.Now = Health(ObsStreamState.Inactive);
        int asked = 0;

        StreamHoldEnd end = await rig.Hold.HoldAsync(
            rig.Hold.Check(),
            () =>
            {
                asked++;
                return Task.FromResult(true);
            },
            CancellationToken.None
        );

        Assert.Equal(StreamHoldEnd.ReleaseStaged, end);
        Assert.Equal(1, asked);
        Assert.Equal(T0 + StreamHold.ReleaseCheckInterval, rig.Clock);
    }

    [Fact]
    public async Task AStop_EndsTheHold()
    {
        Rig rig = Build();
        rig.Obs.Now = Health(ObsStreamState.Inactive);
        using var stop = new CancellationTokenSource();
        int polls = 0;
        rig.OnDelay = () =>
        {
            if (++polls == 3)
            {
                stop.Cancel();
            }
        };

        StreamHoldEnd end = await rig.Hold.HoldAsync(rig.Hold.Check(), null, stop.Token);

        Assert.Equal(StreamHoldEnd.Stopped, end);
        Assert.Null(rig.Status.Read().StreamHoldSince);
    }

    [Fact]
    public async Task ConnectivityLost_HandsTheHoldToTheOutageMode()
    {
        Rig rig = Build();
        rig.Obs.Now = Health(ObsStreamState.Reconnecting);
        rig.OnDelay = () => rig.Connectivity.Online = false;

        StreamHoldEnd end = await rig.Hold.HoldAsync(
            rig.Hold.Check(),
            null,
            CancellationToken.None
        );

        Assert.Equal(StreamHoldEnd.NotDesired, end);
    }

    [Fact]
    public async Task ObsNotAnswering_ShowsTheWaitingSceneOnceItAnswers()
    {
        Rig rig = Build();
        rig.Obs.Script(
            ObsStreamState.Unknown,
            ObsStreamState.Unknown,
            ObsStreamState.Inactive,
            ObsStreamState.Live
        );
        var scenesAt = new List<int>();
        rig.Obs.OnWaitingScene = () => scenesAt.Add(rig.Obs.Checks);

        await rig.Hold.HoldAsync(rig.Hold.Check(), null, CancellationToken.None);

        Assert.Equal(new[] { 3 }, scenesAt);
    }

    [Fact]
    public async Task AHeldSpectate_IsNotDegradedOrStaleForTheSupervisor()
    {
        var time = new ManualClock(T0);
        using var heartbeat = new ServiceHeartbeat(
            new ServiceReadyReport { Role = "spectate", Nonce = "hold396" },
            TimeSpan.FromSeconds(15),
            time,
            root
        );
        heartbeat.Start(CancellationToken.None);
        heartbeat.Work();
        heartbeat.Session(matchProgress: false, "LoadTimedOut");
        Rig rig = Build(recordHold: heartbeat.StreamHold);
        rig.Obs.Now = Health(ObsStreamState.Reconnecting);
        ServiceRoleHealth during = null;
        rig.OnDelay = () =>
        {
            // The heartbeat timer keeps beating through the hold.
            time.Now = rig.Clock;
            if (rig.Clock.Second == T0.Second)
            {
                heartbeat.Beat();
            }

            if (rig.Clock - T0 >= TimeSpan.FromHours(1))
            {
                during ??= Classify(heartbeat.Snapshot(), rig.Clock);
                rig.Obs.Now = Health(ObsStreamState.Live);
            }
        };

        await rig.Hold.HoldAsync(rig.Hold.Check(), null, CancellationToken.None);

        Assert.Equal(ServiceRoleState.Ready, during.State);
        Assert.Equal(ServiceHealthCodes.SpectateStreamHold, during.CauseCode);
        Assert.Null(during.LaunchingSince);
        Assert.Equal(1, during.SessionsWithoutProgress);
        Assert.Equal(T0, during.LastSuccessfulWorkAt);
        Assert.Null(heartbeat.Snapshot().StreamHoldSince);
    }

    [Fact]
    public async Task TheEngine_HoldsBeforeTheNextReplay_AndLoadsItOnceTheStreamIsLive()
    {
        Rig rig = Build();
        rig.Obs.Script(
            ObsStreamState.Inactive,
            ObsStreamState.Inactive,
            ObsStreamState.Reconnecting,
            ObsStreamState.Live
        );
        var game = new ReportingGame(rig);

        await RunEngine(rig, game, 101);

        Assert.Equal(
            new[] { "hold", "unhold", "load", "play 101", "report", "load", "load" },
            rig.Events
        );
        Assert.Equal(1, game.Sessions);
    }

    [Fact]
    public async Task TheEngine_ALiveStream_LoadsWithoutAHold()
    {
        Rig rig = Build();
        rig.Obs.Now = Health(ObsStreamState.Live);
        var game = new ReportingGame(rig);

        await RunEngine(rig, game, 101, 102);

        Assert.Equal(
            new[] { "load", "play 101", "report", "load", "play 102", "report", "load", "load" },
            rig.Events
        );
        Assert.DoesNotContain("hold", rig.Events);
    }

    [Fact]
    public async Task TheEngine_StreamDownMidMatch_FinishesTheMatch_PreloadsNothing_ThenHolds()
    {
        Rig rig = Build();
        rig.Obs.Now = Health(ObsStreamState.Live);
        var game = new ReportingGame(rig)
        {
            // OBS loses the ingest during the first match.
            DuringMatch = id =>
            {
                if (id == 101)
                {
                    rig.Obs.Script(
                        ObsStreamState.Reconnecting,
                        ObsStreamState.Reconnecting,
                        ObsStreamState.Reconnecting,
                        ObsStreamState.Live
                    );
                }
            },
        };

        await RunEngine(rig, game, 101, 102);

        // The match is played to its end, the report preloads nothing, and the next replay
        // loads only after the hold.
        Assert.Equal(
            new[]
            {
                "load",
                "play 101",
                "report",
                "no preload",
                "hold",
                "unhold",
                "load",
                "play 102",
                "report",
                "load",
                "load",
            },
            rig.Events
        );
        Assert.Equal(
            new[] { MatchOutcome.VerifiedCompleted, MatchOutcome.VerifiedCompleted },
            game.Outcomes
        );
    }

    [Fact]
    public async Task TheEngine_DevSettings_NeverHold()
    {
        Rig rig = Build(streaming: false);
        rig.Obs.Now = Health(ObsStreamState.Inactive);
        var game = new ReportingGame(rig);

        await RunEngine(rig, game, 101);

        Assert.DoesNotContain("hold", rig.Events);
        Assert.Equal(1, game.Sessions);
        Assert.Equal(0, rig.Obs.Checks);
    }

    private async Task RunEngine(Rig rig, ReportingGame game, params int[] replayIds)
    {
        var replays = new ScriptedReplays(rig.Events);
        foreach (int id in replayIds)
        {
            replays.Enqueue(id);
        }

        using var heartbeat = new ServiceHeartbeat(
            new ServiceReadyReport { Role = "spectate", Nonce = "engine396" },
            TimeSpan.FromHours(1),
            new ManualClock(T0),
            root
        );
        var engine = new Engine(
            NullLogger<Engine>.Instance,
            game,
            new IdleGameData(),
            replays,
            new CancellationTokenProvider(),
            rig.Status,
            rig.Connectivity,
            new IdleResume(),
            new StubLoader(),
            new NoRelease(),
            heartbeat,
            memoryLog: null,
            rig.Hold
        );

        Assert.True(await engine.RunAsync());
    }

    private Rig Build(
        bool streaming = true,
        bool obsEnabled = true,
        bool armed = true,
        bool holdSetting = true,
        bool online = true,
        bool clientRunning = false,
        Action<DateTimeOffset?, string> recordHold = null
    )
    {
        var rig = new Rig
        {
            Status = new SpectatorStatusStore(
                Path.Combine(root, Guid.NewGuid().ToString("N") + "-status.json")
            ),
            Connectivity = new Watchdog { Online = online },
        };
        rig.Obs = new StreamObs();
        var settings = new AppSettings
        {
            OBS = new OBSSettings
            {
                Enabled = obsEnabled,
                StreamingEnabled = streaming,
                WaitingSceneName = "waiting-screen",
            },
            Spectate = new SpectateSettings { HoldWhileStreamDown = holdSetting },
        };
        rig.Hold = new StreamHold(
            new ListLogger(rig.Log),
            settings,
            rig.Obs,
            rig.Connectivity,
            rig.Status,
            () => armed,
            () => clientRunning,
            () => rig.Clock,
            (span, token) =>
            {
                token.ThrowIfCancellationRequested();
                rig.Clock += span;
                rig.OnDelay?.Invoke();
                return Task.CompletedTask;
            },
            (since, reason) =>
            {
                // One event when the hold starts and one when it ends; the hold refreshes every poll.
                bool holding = rig.Holds.Count > 0 && rig.Holds[^1].Since != null;
                if (since == null)
                {
                    rig.Events.Add("unhold");
                }
                else if (!holding)
                {
                    rig.Events.Add("hold");
                }

                rig.Holds.Add((since, reason));
                recordHold?.Invoke(since, reason);
            }
        );
        return rig;
    }

    private static ServiceRoleHealth Classify(ServiceReadyReport beat, DateTimeOffset now) =>
        ServiceHealthClassifier.Classify(
            "spectate",
            new ServiceProcessRecord
            {
                Name = "spectate",
                Pid = 70,
                Nonce = "hold396",
            },
            running: true,
            beat,
            stopRequested: false,
            now,
            new ServiceHealthSettings()
        );

    private static ObsStreamHealth Health(ObsStreamState state) =>
        state switch
        {
            ObsStreamState.Live => ObsStreamHealth.Next(
                null,
                new ObsStreamSample(true, false, 1000),
                T0
            ),
            ObsStreamState.Inactive => ObsStreamHealth.Next(
                null,
                new ObsStreamSample(false, false, 0),
                T0
            ),
            ObsStreamState.Reconnecting => ObsStreamHealth.Next(
                null,
                new ObsStreamSample(true, true, 199_373_948_174),
                T0
            ),
            ObsStreamState.Stalled => ObsStreamHealth.Next(
                ObsStreamHealth.Next(null, new ObsStreamSample(true, false, 5), T0),
                new ObsStreamSample(true, false, 5),
                T0 + ObsStreamHealth.StallWindow
            ),
            _ => ObsStreamHealth.Unknown(null, "OBS is not running.", T0),
        };

    private sealed class Rig
    {
        public StreamHold Hold { get; set; }
        public StreamObs Obs { get; set; }
        public Watchdog Connectivity { get; init; }
        public SpectatorStatusStore Status { get; init; }
        public DateTimeOffset Clock { get; set; } = T0;
        public Action OnDelay { get; set; }
        public List<string> Events { get; } = new();
        public List<(DateTimeOffset? Since, string Reason)> Holds { get; } = new();
        public List<(LogLevel Level, string Text)> Log { get; } = new();
    }

    /// <summary>OBS as the hold sees it: a scripted stream health, and the waiting scene.</summary>
    private sealed class StreamObs : IObsController
    {
        private readonly Queue<ObsStreamHealth> script = new();

        public ObsStreamHealth Now { get; set; }
        public int Checks { get; private set; }
        public int WaitingScenes { get; private set; }
        public Action OnWaitingScene { get; set; }

        /// <summary>One health per read; the last one stays.</summary>
        public void Script(params ObsStreamState[] states)
        {
            script.Clear();
            foreach (ObsStreamState state in states)
            {
                script.Enqueue(Health(state));
            }
        }

        public ObsStreamHealth CheckStreamHealth()
        {
            Checks++;
            if (script.Count > 0)
            {
                Now = script.Dequeue();
            }

            return Now;
        }

        public ObsStreamHealth ReadStreamHealth() => Now;

        public void SwapToWaitingScene()
        {
            WaitingScenes++;
            OnWaitingScene?.Invoke();
        }

        public void BeginSession() { }

        public void EndSession() { }

        public void ConfigureFromContext() { }

        public Task CycleReportAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void SwapToGameScene() { }

        public void UpdateReplayInfoVisibility(TimeSpan matchTime) { }

        public ObsRecordingResult StartRecording() =>
            ObsRecordingResult.Failed(ObsOutputFailure.NotRequested, "test");

        public ObsRecordingResult StopRecording() =>
            ObsRecordingResult.Failed(ObsOutputFailure.NotRequested, "test");

        public ObsStreamResult StartStreaming() => throw new NotSupportedException();

        public ObsStreamResult StopStreaming() => throw new NotSupportedException();

        public ObsRuntimeSnapshot ReadObsState() => null;
    }

    /// <summary>A game that plays each replay to a verified end and runs its report.</summary>
    private sealed class ReportingGame(Rig rig) : IGameManager
    {
        public int Sessions { get; private set; }
        public List<MatchOutcome> Outcomes { get; } = new();
        public Action<int> DuringMatch { get; init; }
        public MatchOutcome LastOutcome { get; private set; }
        public bool LastMatchClockSeen { get; private set; }

        public async Task<ReplaySessionKind> LaunchAndSpectate(
            LoadedReplay loadedReplay,
            Action<ReplaySessionKind> outcomeKnown,
            Func<Task<LoadedReplay>> whileReporting
        )
        {
            await Task.Yield();
            int id = loadedReplay.ReplayId ?? 0;
            rig.Events.Add("play " + id);
            DuringMatch?.Invoke(id);
            Sessions++;
            LastOutcome = MatchOutcome.VerifiedCompleted;
            LastMatchClockSeen = true;
            Outcomes.Add(LastOutcome);
            outcomeKnown(ReplaySessionKind.Played);
            rig.Events.Add("report");
            LoadedReplay next = await whileReporting().ConfigureAwait(false);
            if (next == null && rig.Events[^1] == "report")
            {
                rig.Events.Add("no preload");
            }

            return ReplaySessionKind.Played;
        }

        public void ReleaseClientAfterDefer() { }
    }

    private sealed class ScriptedReplays(List<string> events) : IReplayProvider
    {
        private readonly Queue<LoadedReplay> waiting = new();

        public bool ContinuesWhenEmpty => false;

        public void Enqueue(int replayId) =>
            waiting.Enqueue(new LoadedReplay { ReplayId = replayId });

        public Task<LoadedReplay> TryLoadNextReplayAsync()
        {
            events.Add("load");
            return Task.FromResult(waiting.Count == 0 ? null : waiting.Dequeue());
        }

        public void Requeue(LoadedReplay replay) => waiting.Enqueue(replay);

        public void Defer(LoadedReplay replay) { }

        public void MarkSpectated(LoadedReplay replay) { }

        public void HoldBack(int replayId) { }
    }

    private sealed class Watchdog : IConnectivityWatchdog
    {
        public bool Online { get; set; } = true;

        public bool IsOnline => Online;

        public ConnectivitySnapshot Last { get; } = new();

        public event EventHandler<ConnectivityChangedEventArgs> Changed
        {
            add { }
            remove { }
        }

        public Task<ConnectivitySnapshot> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Last);

        public bool Apply(ConnectivitySnapshot snapshot) => false;

        public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ListLogger(List<(LogLevel Level, string Text)> lines) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => lines.Add((logLevel, formatter(state, exception)));
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class NoRelease : IReleaseUpdateGate
    {
        public Task<bool> TryStageAsync(CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public bool HandOff() => false;
    }

    private sealed class StubLoader : IReplayLoader
    {
        public Task<Replay> LoadAsync(string path) => Task.FromResult(new Replay());
    }

    private sealed class IdleResume : IReplayResume
    {
        public void Request(int replayId, string replayPath) { }

        public bool HasPending() => false;

        public bool TryTake(out int replayId, out string replayPath)
        {
            replayId = 0;
            replayPath = null;
            return false;
        }
    }

    private sealed class IdleGameData : IGameData
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
