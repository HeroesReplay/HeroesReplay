using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating.Session;
using HeroesReplay.Core.Status;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

/// <summary>
/// Spectate work is match progress only (#170). The engine runs against a scripted game, and the
/// role is classified from the heartbeat it fed.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class SpectateMatchProgressTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ThreeDeferredSessions_LeaveWorkUnchanged_AndDegradeWithTheCause()
    {
        string root = TempDir();
        var clock = new FakeClock(Start);
        try
        {
            using ServiceHeartbeat heartbeat = StartHeartbeat(root, clock);
            heartbeat.Work();
            clock.Now = Start.AddMinutes(9);
            var game = new ScriptedGame(
                (MatchOutcome.LoadTimedOut, false),
                (MatchOutcome.LoadTimedOut, false),
                (MatchOutcome.LoadTimedOut, false)
            );

            await Run(root, game, heartbeat, 101, 102, 103);

            ServiceReadyReport beat = heartbeat.Snapshot();
            Assert.Equal(3, game.Sessions);
            Assert.Equal(Start, beat.LastSuccessfulWorkAt);
            Assert.Equal(3, beat.SessionsWithoutProgress);
            Assert.Equal("LoadTimedOut", beat.LastOutcome);
            Assert.Equal(3, Assert.Single(beat.SessionOutcomes).Value);

            ServiceRoleHealth health = Classify(beat, clock.Now);
            Assert.Equal(ServiceRoleState.Degraded, health.State);
            Assert.Equal(ServiceHealthCodes.Degraded, health.Code);
            Assert.Equal("spectate.no_match_progress", health.CauseCode);
            Assert.Contains("3 replay sessions in a row", health.Cause);
            Assert.Contains("LoadTimedOut", health.Cause);
            Assert.Equal(3, health.SessionsWithoutProgress);
            Assert.Equal("LoadTimedOut", health.LastOutcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OnePlayedSession_RecordsWorkAgain_AndClearsDegraded()
    {
        string root = TempDir();
        var clock = new FakeClock(Start);
        try
        {
            using ServiceHeartbeat heartbeat = StartHeartbeat(root, clock);
            clock.Now = Start.AddMinutes(9);
            var deferring = new ScriptedGame(
                (MatchOutcome.LoadTimedOut, false),
                (MatchOutcome.ClientCrashed, false),
                (MatchOutcome.LoadTimedOut, false)
            );
            await Run(root, deferring, heartbeat, 101, 102, 103);
            Assert.Equal(
                ServiceRoleState.Degraded,
                Classify(heartbeat.Snapshot(), clock.Now).State
            );

            clock.Now = Start.AddMinutes(12);
            var played = new ScriptedGame((MatchOutcome.VerifiedCompleted, true));
            await Run(root, played, heartbeat, 104);

            ServiceReadyReport beat = heartbeat.Snapshot();
            Assert.Equal(Start.AddMinutes(12), beat.LastSuccessfulWorkAt);
            Assert.Equal(0, beat.SessionsWithoutProgress);
            Assert.Equal("VerifiedCompleted", beat.LastOutcome);
            Assert.Equal(2, beat.SessionOutcomes["LoadTimedOut"]);
            Assert.Equal(1, beat.SessionOutcomes["ClientCrashed"]);
            Assert.Equal(1, beat.SessionOutcomes["VerifiedCompleted"]);
            ServiceRoleHealth health = Classify(beat, clock.Now);
            Assert.Equal(ServiceRoleState.Ready, health.State);
            Assert.Null(health.CauseCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HeldSessions_AreNotWork()
    {
        string root = TempDir();
        var clock = new FakeClock(Start);
        try
        {
            using ServiceHeartbeat heartbeat = StartHeartbeat(root, clock);
            var game = new ScriptedGame(
                (MatchOutcome.BuildNotInstalled, false),
                (MatchOutcome.BuildNotInstalled, false)
            );

            await Run(root, game, heartbeat, 101, 102);

            ServiceReadyReport beat = heartbeat.Snapshot();
            Assert.Null(beat.LastSuccessfulWorkAt);
            Assert.Equal(2, beat.SessionsWithoutProgress);
            Assert.Equal(ServiceRoleState.Ready, Classify(beat, clock.Now).State);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ClockAdvance_IsWork_AndEndsTheRun()
    {
        string root = TempDir();
        var clock = new FakeClock(Start);
        try
        {
            using ServiceHeartbeat heartbeat = StartHeartbeat(root, clock);
            heartbeat.Session(matchProgress: false, "LoadTimedOut");
            heartbeat.Session(matchProgress: false, "LoadTimedOut");
            Assert.Equal(2, heartbeat.Snapshot().SessionsWithoutProgress);

            clock.Now = Start.AddSeconds(30);
            heartbeat.Work();

            ServiceReadyReport beat = heartbeat.Snapshot();
            Assert.Equal(0, beat.SessionsWithoutProgress);
            Assert.Equal(Start.AddSeconds(30), beat.LastSuccessfulWorkAt);
            Assert.Equal("LoadTimedOut", beat.LastOutcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OtherRoles_NeverWriteTheSessionRun()
    {
        string root = TempDir();
        var clock = new FakeClock(Start);
        try
        {
            using var heartbeat = new ServiceHeartbeat(
                new ServiceReadyReport { Role = "download", Nonce = "dl170" },
                TimeSpan.FromHours(1),
                clock,
                root
            );
            heartbeat.Start(CancellationToken.None);
            heartbeat.Work();

            Assert.Null(heartbeat.Snapshot().SessionsWithoutProgress);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task Run(
        string root,
        ScriptedGame game,
        ServiceHeartbeat heartbeat,
        params int[] replayIds
    )
    {
        var provider = new ScriptedReplays();
        foreach (int id in replayIds)
        {
            provider.Enqueue(id);
        }

        var engine = new Engine(
            NullLogger<Engine>.Instance,
            game,
            new IdleGameData(),
            provider,
            new CancellationTokenProvider(),
            new SpectatorStatusStore(Path.Combine(root, "status.json")),
            new IdleWatchdog(),
            new IdleResume(),
            new StubLoader(),
            new NoRelease(),
            heartbeat
        );

        Assert.True(await engine.RunAsync());
    }

    private static ServiceHeartbeat StartHeartbeat(string root, FakeClock clock)
    {
        var heartbeat = new ServiceHeartbeat(
            new ServiceReadyReport { Role = "spectate", Nonce = "spec170" },
            TimeSpan.FromHours(1),
            clock,
            root
        );
        heartbeat.Start(CancellationToken.None);
        return heartbeat;
    }

    private static ServiceRoleHealth Classify(ServiceReadyReport beat, DateTimeOffset now) =>
        ServiceHealthClassifier.Classify(
            "spectate",
            new ServiceProcessRecord
            {
                Name = "spectate",
                Pid = 70,
                Nonce = "spec170",
            },
            running: true,
            beat,
            stopRequested: false,
            now,
            new ServiceHealthSettings()
        );

    private static string TempDir()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-progress-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FakeClock : TimeProvider
    {
        public FakeClock(DateTimeOffset now)
        {
            Now = now;
        }

        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ScriptedGame : IGameManager
    {
        private readonly Queue<(MatchOutcome Outcome, bool ClockSeen)> script;

        public ScriptedGame(params (MatchOutcome Outcome, bool ClockSeen)[] sessions)
        {
            script = new Queue<(MatchOutcome, bool)>(sessions);
        }

        public int Sessions { get; private set; }

        public MatchOutcome LastOutcome { get; private set; }

        public bool LastMatchClockSeen { get; private set; }

        public async Task<ReplaySessionKind> LaunchAndSpectate(
            LoadedReplay loadedReplay,
            Func<Task<LoadedReplay>> whileReporting
        )
        {
            await Task.Yield();
            (LastOutcome, LastMatchClockSeen) = script.Dequeue();
            Sessions++;
            return ReplaySession.Classify(LastOutcome);
        }

        public void ReleaseClientAfterDefer() { }
    }

    private sealed class ScriptedReplays : IReplayProvider
    {
        private readonly Queue<LoadedReplay> waiting = new();

        public bool ContinuesWhenEmpty => false;

        public void Enqueue(int replayId) =>
            waiting.Enqueue(new LoadedReplay { ReplayId = replayId });

        public Task<LoadedReplay> TryLoadNextReplayAsync() =>
            Task.FromResult(waiting.Count == 0 ? null : waiting.Dequeue());

        public void Requeue(LoadedReplay replay) { }

        public void Defer(LoadedReplay replay) { }

        public void MarkSpectated(LoadedReplay replay) { }

        public void HoldBack(int replayId) { }
    }

    private sealed class NoRelease : IReleaseUpdateGate
    {
        public Task<bool> TryStageAsync(CancellationToken cancellationToken) =>
            Task.FromResult(false);
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

    private sealed class IdleWatchdog : IConnectivityWatchdog
    {
        public bool IsOnline => true;

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
