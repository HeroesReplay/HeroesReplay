using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating.Session;
using HeroesReplay.Core.Status;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.SelfUpdate;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReleaseHandoffTests
{
    [Fact]
    public async Task Update_PutsThePreloadedReplayBackSoTheNextProcessPlaysIt()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-handoff-" + Path.GetRandomFileName());
        string data = Path.Combine(root, "Data");
        string standard = Path.Combine(data, "Standard");
        try
        {
            Directory.CreateDirectory(standard);
            File.WriteAllText(Path.Combine(standard, "101.StormReplay"), "a");
            File.WriteAllText(Path.Combine(standard, "202.StormReplay"), "b");
            File.WriteAllText(Path.Combine(standard, "303.StormReplay"), "c");
            File.WriteAllText(Path.Combine(data, "spectated-ids.txt"), string.Empty);

            AppSettings settings = CacheSettings(data);
            var game = new RecordingGame();
            var engine = new Engine(
                NullLogger<Engine>.Instance,
                game,
                new IdleGameData(),
                Cache(settings),
                new CancellationTokenProvider(),
                new SpectatorStatusStore(Path.Combine(root, "status.json")),
                new IdleWatchdog(),
                new IdleResume(),
                new StubLoader(),
                new FlagGate(stage: true)
            );

            await engine.RunAsync();

            Assert.Equal(new int?[] { 101 }, game.Spectated.ToArray());
            Assert.Equal(
                new[] { "101" },
                File.ReadAllLines(Path.Combine(data, "spectated-ids.txt"))
            );

            LoadedReplay next = await Cache(settings).TryLoadNextReplayAsync();
            Assert.Equal(202, next.ReplayId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task NoUpdate_PlaysThePreloadedReplayInThisProcess()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-handoff-" + Path.GetRandomFileName());
        var provider = new ScriptedReplays(continuesWhenEmpty: false);
        provider.Enqueue(101);
        provider.Enqueue(202);
        var game = new RecordingGame();
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
            new FlagGate(stage: false)
        );

        try
        {
            Assert.True(await engine.RunAsync());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        Assert.Equal(new int?[] { 101, 202 }, game.Spectated.ToArray());
        Assert.Equal(new[] { 101, 202 }, provider.SpectatedIds.ToArray());
        Assert.Empty(provider.Requeued);
    }

    [Fact]
    public async Task UnexpectedEngineError_IsReportedSoSpectateExitsNonZero()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-handoff-" + Path.GetRandomFileName());
        var provider = new ScriptedReplays(continuesWhenEmpty: false);
        provider.Enqueue(101);
        var game = new RecordingGame();
        var engine = new Engine(
            NullLogger<Engine>.Instance,
            game,
            new IdleGameData { LoadError = new InvalidOperationException("no hero data") },
            provider,
            new CancellationTokenProvider(),
            new SpectatorStatusStore(Path.Combine(root, "status.json")),
            new IdleWatchdog(),
            new IdleResume(),
            new StubLoader(),
            new FlagGate(stage: false)
        );

        try
        {
            Assert.False(await engine.RunAsync());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        Assert.Empty(game.Spectated);
    }

    [Fact]
    public async Task PlayOnce_ReturnsWhileTheWatchdogIsStillWaiting()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-handoff-" + Path.GetRandomFileName());
        var provider = new ScriptedReplays(continuesWhenEmpty: false);
        provider.Enqueue(101);
        var game = new RecordingGame();
        var engine = new Engine(
            NullLogger<Engine>.Instance,
            game,
            new IdleGameData(),
            provider,
            new CancellationTokenProvider(),
            new SpectatorStatusStore(Path.Combine(root, "status.json")),
            new BlockingWatchdog(),
            new IdleResume(),
            new StubLoader(),
            new FlagGate(stage: false)
        );

        try
        {
            Task run = engine.RunAsync();
            Task finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(3)));
            Assert.Same(run, finished);
            await run;
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        Assert.Equal(new int?[] { 101 }, game.Spectated.ToArray());
    }

    [Fact]
    public async Task ContinuousQueue_StaysUpUntilTheConsoleTokenIsCancelled()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-handoff-" + Path.GetRandomFileName());
        using var cancel = new CancellationTokenSource();
        var provider = new ScriptedReplays(continuesWhenEmpty: true);
        var engine = new Engine(
            NullLogger<Engine>.Instance,
            new RecordingGame(),
            new IdleGameData(),
            provider,
            new CancellationTokenProvider(cancel.Token),
            new SpectatorStatusStore(Path.Combine(root, "status.json")),
            new BlockingWatchdog(),
            new IdleResume(),
            new StubLoader(),
            new FlagGate(stage: false)
        );

        try
        {
            Task run = engine.RunAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            Assert.False(run.IsCompleted);
            cancel.Cancel();
            Task finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(3)));
            Assert.Same(run, finished);
            await run;
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UnplayedReplay_StaysQueuedAndSpectateKeepsRunning()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-handoff-" + Path.GetRandomFileName());
        using var cancel = new CancellationTokenSource();
        var provider = new ScriptedReplays(continuesWhenEmpty: true);
        provider.Enqueue(101);
        var game = new RecordingGame { Outcome = MatchOutcome.LoadTimedOut };
        var engine = new Engine(
            NullLogger<Engine>.Instance,
            game,
            new IdleGameData(),
            provider,
            new CancellationTokenProvider(cancel.Token),
            new SpectatorStatusStore(Path.Combine(root, "status.json")),
            new BlockingWatchdog(),
            new IdleResume(),
            new StubLoader(),
            new FlagGate(stage: false)
        );

        try
        {
            Task run = engine.RunAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            Assert.False(run.IsCompleted);
            cancel.Cancel();
            Task finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(3)));
            Assert.Same(run, finished);
            await run;
            Assert.Equal(new[] { 101 }, provider.Deferred.ToArray());
            Assert.Empty(provider.Requeued);
            Assert.Empty(provider.SpectatedIds);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HeldReplay_StaysQueuedAndDoesNotMarkSpectated()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-handoff-" + Path.GetRandomFileName());
        using var cancel = new CancellationTokenSource();
        var provider = new ScriptedReplays(continuesWhenEmpty: true);
        provider.Enqueue(101);
        var status = new SpectatorStatusStore(Path.Combine(root, "status.json"));
        var game = new RecordingGame
        {
            Outcome = MatchOutcome.VersionMismatch,
            Status = status,
            Winner = 0,
        };
        var engine = new Engine(
            NullLogger<Engine>.Instance,
            game,
            new IdleGameData(),
            provider,
            new CancellationTokenProvider(cancel.Token),
            status,
            new BlockingWatchdog(),
            new IdleResume(),
            new StubLoader(),
            new FlagGate(stage: false)
        );

        try
        {
            Task run = engine.RunAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            Assert.False(run.IsCompleted);
            cancel.Cancel();
            Task finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(3)));
            Assert.Same(run, finished);
            await run;
            Assert.Empty(provider.Deferred);
            Assert.Equal(new[] { 101 }, provider.Requeued.ToArray());
            Assert.Empty(provider.SpectatedIds);
            SpectatorStatus read = status.Read();
            Assert.Null(read.CompletedAt);
            Assert.Null(read.CompletedReplayId);
            Assert.Null(read.CompletedWinnerTeam);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ClockSeenCrash_StaysQueuedAndPublishesNoWinner()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-handoff-" + Path.GetRandomFileName());
        using var cancel = new CancellationTokenSource();
        var provider = new ScriptedReplays(continuesWhenEmpty: true);
        provider.Enqueue(101);
        var status = new SpectatorStatusStore(Path.Combine(root, "status.json"));
        var game = new RecordingGame
        {
            Outcome = MatchOutcome.ClientCrashed,
            Status = status,
            Winner = 1,
        };
        var engine = new Engine(
            NullLogger<Engine>.Instance,
            game,
            new IdleGameData(),
            provider,
            new CancellationTokenProvider(cancel.Token),
            status,
            new BlockingWatchdog(),
            new IdleResume(),
            new StubLoader(),
            new FlagGate(stage: false)
        );

        try
        {
            Task run = engine.RunAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            Assert.False(run.IsCompleted);
            cancel.Cancel();
            Task finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(3)));
            Assert.Same(run, finished);
            await run;
            Assert.Equal(new[] { 101 }, provider.Deferred.ToArray());
            Assert.Empty(provider.Requeued);
            Assert.Empty(provider.SpectatedIds);
            SpectatorStatus read = status.Read();
            Assert.Null(read.CompletedAt);
            Assert.Null(read.CompletedReplayId);
            Assert.Null(read.CompletedWinnerTeam);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task VerifiedCompletion_MarksSpectatedAndPublishesOneWinner()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-handoff-" + Path.GetRandomFileName());
        var provider = new ScriptedReplays(continuesWhenEmpty: false);
        provider.Enqueue(101);
        var status = new SpectatorStatusStore(Path.Combine(root, "status.json"));
        var game = new RecordingGame
        {
            Outcome = MatchOutcome.VerifiedCompleted,
            Status = status,
            Winner = 0,
        };
        var engine = new Engine(
            NullLogger<Engine>.Instance,
            game,
            new IdleGameData(),
            provider,
            new CancellationTokenProvider(),
            status,
            new IdleWatchdog(),
            new IdleResume(),
            new StubLoader(),
            new FlagGate(stage: false)
        );

        try
        {
            await engine.RunAsync();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        Assert.Equal(new int?[] { 101 }, game.Spectated.ToArray());
        Assert.Equal(new[] { 101 }, provider.SpectatedIds.ToArray());
        Assert.Equal(new[] { 101 }, provider.Held.ToArray());
        Assert.Empty(provider.Requeued);
        SpectatorStatus read = status.Read();
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero), read.CompletedAt);
        Assert.Equal(101, read.CompletedReplayId);
        Assert.Equal(0, read.CompletedWinnerTeam);
    }

    [Fact]
    public async Task StopDuringTheReport_TheNextProcessDoesNotPlayTheFinishedReplayAgain()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-handoff-" + Path.GetRandomFileName());
        string data = Path.Combine(root, "Data");
        string standard = Path.Combine(data, "Standard");
        string spectated = Path.Combine(data, "spectated-ids.txt");
        using var stop = new CancellationTokenSource();
        try
        {
            Directory.CreateDirectory(standard);
            File.WriteAllText(Path.Combine(standard, "101.StormReplay"), "a");
            File.WriteAllText(Path.Combine(standard, "202.StormReplay"), "b");
            File.WriteAllText(spectated, string.Empty);

            AppSettings settings = CacheSettings(data);
            var game = new ReportingGame(MatchOutcome.VerifiedCompleted, stop.Token);
            var engine = new Engine(
                NullLogger<Engine>.Instance,
                game,
                new IdleGameData(),
                Cache(settings),
                new CancellationTokenProvider(stop.Token),
                new SpectatorStatusStore(Path.Combine(root, "status.json")),
                new BlockingWatchdog(),
                new IdleResume(),
                new StubLoader(),
                new FlagGate(stage: false)
            );

            Task run = engine.RunAsync();
            await game.Reporting.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // The report scenes are up and the next replay is loaded. A kill here must not lose 101.
            Assert.Equal(new[] { "101" }, File.ReadAllLines(spectated));

            stop.Cancel();
            await run.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(new[] { "101" }, File.ReadAllLines(spectated));
            LoadedReplay next = await Cache(settings).TryLoadNextReplayAsync();
            Assert.Equal(202, next.ReplayId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(MatchOutcome.AwardScreen)]
    [InlineData(MatchOutcome.VersionMismatch)]
    [InlineData(MatchOutcome.BuildNotInstalled)]
    [InlineData(MatchOutcome.LoadTimedOut)]
    [InlineData(MatchOutcome.ClientCrashed)]
    [InlineData(MatchOutcome.Stopped)]
    public async Task OutcomeThatIsNotFinal_IsNeverMarkedSpectated(MatchOutcome outcome)
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-handoff-" + Path.GetRandomFileName());
        string data = Path.Combine(root, "Data");
        string standard = Path.Combine(data, "Standard");
        string spectated = Path.Combine(data, "spectated-ids.txt");
        using var stop = new CancellationTokenSource();
        try
        {
            Directory.CreateDirectory(standard);
            File.WriteAllText(Path.Combine(standard, "101.StormReplay"), "a");
            File.WriteAllText(spectated, string.Empty);

            AppSettings settings = CacheSettings(data);
            var game = new ReportingGame(outcome, stop.Token);
            var engine = new Engine(
                NullLogger<Engine>.Instance,
                game,
                new IdleGameData(),
                Cache(settings),
                new CancellationTokenProvider(stop.Token),
                new SpectatorStatusStore(Path.Combine(root, "status.json")),
                new BlockingWatchdog(),
                new IdleResume(),
                new StubLoader(),
                new FlagGate(stage: false)
            );

            Task run = engine.RunAsync();
            await game.Reporting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(File.ReadAllLines(spectated));

            stop.Cancel();
            await run.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(new int?[] { 101 }, game.Spectated.ToArray());
            Assert.Empty(File.ReadAllLines(spectated));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HoldBack_SkipsTheReplayStillInSession()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-hold-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Requests"));
        Directory.CreateDirectory(Path.Combine(root, "Standard"));
        File.WriteAllBytes(
            Path.Combine(root, "Requests", "10_Quick Match_Map_.StormReplay"),
            Array.Empty<byte>()
        );
        File.WriteAllBytes(
            Path.Combine(root, "Requests", "20_Quick Match_Map_.StormReplay"),
            Array.Empty<byte>()
        );
        AppSettings settings = CacheSettings(root);
        try
        {
            ReplayCacheProvider plain = Cache(settings);
            LoadedReplay lowest = await plain.TryLoadNextReplayAsync();
            Assert.Equal(10, lowest.ReplayId);

            ReplayCacheProvider held = Cache(settings);
            held.Requeue(new LoadedReplay { ReplayId = 10 });
            held.HoldBack(10);
            LoadedReplay next = await held.TryLoadNextReplayAsync();
            Assert.Equal(20, next.ReplayId);

            held.MarkSpectated(new LoadedReplay { ReplayId = 10 });
            LoadedReplay after = await held.TryLoadNextReplayAsync();
            Assert.Null(after);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SpectateFailure_LeavesTheProcessRunning()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-handoff-" + Path.GetRandomFileName());
        using var cancel = new CancellationTokenSource();
        var provider = new ScriptedReplays(continuesWhenEmpty: true);
        provider.Enqueue(101);
        var game = new RecordingGame { ThrowTimes = 1 };
        var engine = new Engine(
            NullLogger<Engine>.Instance,
            game,
            new IdleGameData(),
            provider,
            new CancellationTokenProvider(cancel.Token),
            new SpectatorStatusStore(Path.Combine(root, "status.json")),
            new BlockingWatchdog(),
            new IdleResume(),
            new StubLoader(),
            new FlagGate(stage: false)
        );

        try
        {
            Task run = engine.RunAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            Assert.False(run.IsCompleted);
            cancel.Cancel();
            Task finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(3)));
            Assert.Same(run, finished);
            await run;
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static AppSettings CacheSettings(string data) =>
        new()
        {
            Location = new LocationSettings { DataDirectory = data },
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                StandardCacheDirectoryName = "Standard",
                RequestsCacheDirectoryName = "Requests",
            },
            StormReplay = new StormReplaySettings { Seperator = "_" },
        };

    private static ReplayCacheProvider Cache(AppSettings settings) =>
        new(
            NullLogger<ReplayCacheProvider>.Instance,
            new StubLoader(),
            new ReplayHelper(NullLogger<ReplayHelper>.Instance, settings),
            new IdleHeroesProfile(),
            new CancellationTokenProvider(),
            settings
        );

    private sealed class FlagGate : IReleaseUpdateGate
    {
        private readonly bool stage;

        public FlagGate(bool stage)
        {
            this.stage = stage;
        }

        public Task<bool> TryStageAsync(CancellationToken cancellationToken) =>
            Task.FromResult(stage);
    }

    private sealed class RecordingGame : IGameManager
    {
        public List<int?> Spectated { get; } = new();

        public int ThrowTimes { get; set; }

        public MatchOutcome Outcome { get; set; } = MatchOutcome.VerifiedCompleted;

        public SpectatorStatusStore Status { get; set; }

        public int? Winner { get; set; } = 1;

        public async Task<ReplaySessionKind> LaunchAndSpectate(
            LoadedReplay loadedReplay,
            Action<ReplaySessionKind> outcomeKnown,
            Func<Task<LoadedReplay>> whileReporting
        )
        {
            // A completed task would keep a requeue spinning on the caller and the test could not cancel.
            await Task.Yield();
            if (ThrowTimes > 0)
            {
                ThrowTimes--;
                throw new InvalidOperationException("game process disappeared");
            }

            Spectated.Add(loadedReplay.ReplayId);
            var completion = new MatchCompletion();
            DateTimeOffset completedAt = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
            Status?.Patch(item =>
            {
                completion.Apply(item, Outcome, loadedReplay.ReplayId, Winner, completedAt);
                completion.Apply(
                    item,
                    Outcome,
                    loadedReplay.ReplayId,
                    Winner == 0 ? 1 : 0,
                    completedAt.AddHours(1)
                );
            });
            ReplaySessionKind kind = ReplaySession.Classify(Outcome);
            outcomeKnown(kind);
            if (kind == ReplaySessionKind.Played)
            {
                await whileReporting().ConfigureAwait(false);
            }

            return kind;
        }

        public void ReleaseClientAfterDefer() { }

        public MatchOutcome LastOutcome => Outcome;

        public bool LastMatchClockSeen => Outcome == MatchOutcome.VerifiedCompleted;
    }

    /// <summary>
    /// Hears its outcome the way GameManager does, loads the next replay, then stays in the
    /// report scenes until spectate is stopped.
    /// </summary>
    private sealed class ReportingGame : IGameManager
    {
        private readonly CancellationToken stop;

        public ReportingGame(MatchOutcome outcome, CancellationToken stop)
        {
            Outcome = outcome;
            this.stop = stop;
        }

        public MatchOutcome Outcome { get; }

        public List<int?> Spectated { get; } = new();

        public TaskCompletionSource Reporting { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ReplaySessionKind> LaunchAndSpectate(
            LoadedReplay loadedReplay,
            Action<ReplaySessionKind> outcomeKnown,
            Func<Task<LoadedReplay>> whileReporting
        )
        {
            await Task.Yield();
            Spectated.Add(loadedReplay.ReplayId);
            ReplaySessionKind kind = ReplaySession.Classify(Outcome);
            outcomeKnown(kind);
            if (!ReplaySession.StaysQueued(kind))
            {
                await whileReporting().ConfigureAwait(false);
            }

            Reporting.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }

            return kind;
        }

        public void ReleaseClientAfterDefer() { }

        public MatchOutcome LastOutcome => Outcome;

        public bool LastMatchClockSeen => Outcome == MatchOutcome.VerifiedCompleted;
    }

    private sealed class ScriptedReplays : IReplayProvider
    {
        private readonly Queue<LoadedReplay> waiting = new();
        private LoadedReplay staged;

        public ScriptedReplays(bool continuesWhenEmpty)
        {
            ContinuesWhenEmpty = continuesWhenEmpty;
        }

        public List<int> Requeued { get; } = new();

        public bool ContinuesWhenEmpty { get; }

        public void Enqueue(int replayId)
        {
            waiting.Enqueue(new LoadedReplay { ReplayId = replayId });
        }

        public Task<LoadedReplay> TryLoadNextReplayAsync()
        {
            if (staged != null)
            {
                LoadedReplay ready = staged;
                staged = null;
                return Task.FromResult(ready);
            }

            return Task.FromResult(waiting.Count == 0 ? null : waiting.Dequeue());
        }

        public List<int> SpectatedIds { get; } = new();

        public List<int> Held { get; } = new();

        public void HoldBack(int replayId)
        {
            if (replayId > 0)
            {
                Held.Add(replayId);
            }
        }

        public void Requeue(LoadedReplay replay)
        {
            if (replay?.ReplayId is not int replayId)
            {
                return;
            }

            Requeued.Add(replayId);
            staged = replay;
        }

        public List<int> Deferred { get; } = new();

        public void Defer(LoadedReplay replay)
        {
            if (replay?.ReplayId is not int replayId)
            {
                return;
            }

            Deferred.Add(replayId);
            if (staged?.ReplayId == replayId)
            {
                staged = null;
            }
        }

        public void MarkSpectated(LoadedReplay replay)
        {
            if (replay?.ReplayId is int replayId)
            {
                SpectatedIds.Add(replayId);
            }
        }
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

    private sealed class BlockingWatchdog : IConnectivityWatchdog
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

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException) { }
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

        public Exception LoadError { get; init; }

        public Task LoadDataAsync() =>
            LoadError == null ? Task.CompletedTask : Task.FromException(LoadError);
    }

    private sealed class IdleHeroesProfile : IHeroesProfileService
    {
        public Task<int> GetMaxReplayIdAsync() => Task.FromResult(0);

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByFilters(
            GameType? gameType = null,
            GameRank? gameRank = null,
            string gameMap = null
        ) => Task.FromResult<IEnumerable<HeroesProfileReplay>>(Array.Empty<HeroesProfileReplay>());

        public Task<HeroesProfileReplay> GetReplayByIdAsync(int replayId) =>
            Task.FromResult<HeroesProfileReplay>(null);

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByMinId(int minId) =>
            Task.FromResult<IEnumerable<HeroesProfileReplay>>(Array.Empty<HeroesProfileReplay>());

        public Task<ReplayListing> ListPageAsync(int minId) => Task.FromResult(ReplayListing.Empty);

        public Task<IReadOnlyList<HeroesProfileReplay>> ListAfterAsync(
            int after,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<IReadOnlyList<HeroesProfileReplay>>(Array.Empty<HeroesProfileReplay>());

        public Task DownloadReplayAsync(
            int replayId,
            Stream destination,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task EnrichRankAsync(
            HeroesProfileReplay replay,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }
}
