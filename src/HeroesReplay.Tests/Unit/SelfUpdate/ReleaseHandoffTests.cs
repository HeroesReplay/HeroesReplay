using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Connectivity;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Observer;
using HeroesReplay.Core.Services.Providers;
using HeroesReplay.Core.Services.SelfUpdate;
using HeroesReplay.Core.Services.Shared;
using HeroesReplay.Core.Services.Status;
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
            await engine.RunAsync();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        Assert.Equal(new int?[] { 101, 202 }, game.Spectated.ToArray());
        Assert.Empty(provider.Requeued);
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

        public Task LaunchAndSpectate(LoadedReplay loadedReplay, Func<Task> whileReporting)
        {
            Spectated.Add(loadedReplay.ReplayId);
            return whileReporting();
        }
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

        public void Requeue(LoadedReplay replay)
        {
            if (replay?.ReplayId is not int replayId)
            {
                return;
            }

            Requeued.Add(replayId);
            staged = replay;
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

        public Task LoadDataAsync() => Task.CompletedTask;
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
