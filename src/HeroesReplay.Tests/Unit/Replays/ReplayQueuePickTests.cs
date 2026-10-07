using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Replays;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayQueuePickTests
{
    private static readonly string[] Installed = { "2.55.17.98025", "2.57.0.98304" };

    [Fact]
    public void CanLaunch_SkipsANewerOrHeldBuildAndKeepsAnInstalledOrDownloadableOne()
    {
        Assert.False(ReplayQueuePick.CanLaunch("2.57.0.98400", Installed));
        Assert.False(
            ReplayQueuePick.CanLaunch("2.57.0.98285", Installed, new[] { "2.57.0.98285" })
        );
        Assert.True(ReplayQueuePick.CanLaunch("2.57.0.98285", Installed));
        Assert.True(ReplayQueuePick.CanLaunch("2.57.0.98297", Installed));
        Assert.True(ReplayQueuePick.CanLaunch("2.57.0.98304", Installed));
        Assert.True(ReplayQueuePick.CanLaunch("2.55.17.98025", Installed));
    }

    [Fact]
    public async Task TryLoadNext_PlaysTheNewestOrdinaryReplayFirst()
    {
        // Production on 2026-10-07 held 768 cached replays from before 2.57.0.98348. Oldest first
        // played that backlog for days before any current-patch replay.
        string root = Path.Combine(Path.GetTempPath(), "hr-queue-new-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Standard"));
        Directory.CreateDirectory(Path.Combine(root, "Requests"));
        File.WriteAllText(Path.Combine(root, SpectateQueue.SpectatedFileName), string.Empty);
        foreach (int id in new[] { 10, 20, 30 })
        {
            File.WriteAllBytes(
                Path.Combine(root, "Standard", id + "_Storm League_Map_.StormReplay"),
                new byte[] { 1 }
            );
        }

        var settings = new AppSettings
        {
            Location = new LocationSettings { DataDirectory = root },
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                StandardCacheDirectoryName = "Standard",
                RequestsCacheDirectoryName = "Requests",
            },
            StormReplay = new StormReplaySettings { Seperator = "_" },
            Spectate = new SpectateSettings { MinimumGameVersion = "2.57.0.98285" },
        };
        var provider = new ReplayCacheProvider(
            NullLogger<ReplayCacheProvider>.Instance,
            new VersionLoader(
                new Dictionary<int, string>
                {
                    [10] = "2.57.0.98285",
                    [20] = "2.57.0.98304",
                    [30] = "2.57.0.98348",
                }
            ),
            new ReplayHelper(NullLogger<ReplayHelper>.Instance, settings),
            new IdleProfile(),
            new CancellationTokenProvider(),
            settings
        );
        provider.UseInstalledVersions(() => new[] { "2.57.0.98304", "2.57.0.98348" });

        try
        {
            LoadedReplay first = await provider.TryLoadNextReplayAsync();
            Assert.Equal(30, first.ReplayId);
            Assert.Equal("2.57.0.98348", first.Replay.ReplayVersion);
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
    [InlineData(false, 30)]
    [InlineData(true, 10)]
    public async Task TryLoadNext_TakesAMissingOlderBuildUnlessItsDownloadFailed(
        bool held,
        int expected
    )
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-queue-dl-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Standard"));
        Directory.CreateDirectory(Path.Combine(root, "Requests"));
        File.WriteAllText(Path.Combine(root, SpectateQueue.SpectatedFileName), string.Empty);
        foreach (int id in new[] { 10, 30 })
        {
            File.WriteAllBytes(
                Path.Combine(root, "Standard", id + "_Storm League_Map_.StormReplay"),
                new byte[] { 1 }
            );
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (held)
        {
            ClientDownloadHold.Record(
                ClientDownloadHold.FilePath(root),
                "2.57.0.98285",
                now.AddMinutes(-5),
                TimeSpan.FromHours(4)
            );
        }

        var settings = new AppSettings
        {
            Location = new LocationSettings { DataDirectory = root },
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                StandardCacheDirectoryName = "Standard",
                RequestsCacheDirectoryName = "Requests",
            },
            StormReplay = new StormReplaySettings { Seperator = "_" },
            Spectate = new SpectateSettings { MinimumGameVersion = "2.57.0.98285" },
        };
        var provider = new ReplayCacheProvider(
            NullLogger<ReplayCacheProvider>.Instance,
            new VersionLoader(
                new Dictionary<int, string> { [10] = "2.57.0.98304", [30] = "2.57.0.98285" }
            ),
            new ReplayHelper(NullLogger<ReplayHelper>.Instance, settings),
            new IdleProfile(),
            new CancellationTokenProvider(),
            settings
        );
        provider.UseInstalledVersions(() => new[] { "2.57.0.98304", "2.57.0.98348" });
        provider.UseClock(() => now);

        try
        {
            LoadedReplay first = await provider.TryLoadNextReplayAsync();
            Assert.Equal(expected, first.ReplayId);
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
    public async Task TryLoadNext_SkipsMissingBuildsAndReturnsOneThatCanLaunch()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-queue-pick-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Standard"));
        Directory.CreateDirectory(Path.Combine(root, "Requests"));
        File.WriteAllText(Path.Combine(root, SpectateQueue.SpectatedFileName), string.Empty);
        File.WriteAllText(
            Path.Combine(root, SpectateQueue.BelowFloorFileName),
            "20" + Environment.NewLine
        );
        File.WriteAllBytes(
            Path.Combine(root, "Standard", "40_Storm League_Map_.StormReplay"),
            new byte[] { 1 }
        );
        File.WriteAllBytes(
            Path.Combine(root, "Standard", "20_Storm League_Map_.StormReplay"),
            new byte[] { 1 }
        );
        File.WriteAllBytes(
            Path.Combine(root, "Standard", "30_Storm League_Map_.StormReplay"),
            new byte[] { 1 }
        );
        var settings = new AppSettings
        {
            Location = new LocationSettings { DataDirectory = root },
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                StandardCacheDirectoryName = "Standard",
                RequestsCacheDirectoryName = "Requests",
            },
            StormReplay = new StormReplaySettings { Seperator = "_" },
            Spectate = new SpectateSettings { MinimumGameVersion = "2.57.0.98285" },
        };
        var loader = new VersionLoader(
            new Dictionary<int, string>
            {
                [40] = "2.57.0.98400",
                [20] = "2.55.17.98025",
                [30] = "2.57.0.98304",
            }
        );
        var provider = new ReplayCacheProvider(
            NullLogger<ReplayCacheProvider>.Instance,
            loader,
            new ReplayHelper(NullLogger<ReplayHelper>.Instance, settings),
            new IdleProfile(),
            new CancellationTokenProvider(),
            settings
        );
        provider.UseInstalledVersions(() => Installed);

        try
        {
            LoadedReplay first = await provider.TryLoadNextReplayAsync();
            Assert.Equal(30, first.ReplayId);
            Assert.Equal("2.57.0.98304", first.Replay.ReplayVersion);
            Assert.Equal(2, loader.Loads);
            string lease30 = Path.Combine(root, "leases", "30.txt");
            string leased = File.ReadAllText(lease30);
            Assert.Contains("state=Leased", leased);
            Assert.Contains("replay=30", leased);
            Assert.True(ReplayLease.Take(lease30, 30));
            string reread = File.ReadAllText(lease30);
            Assert.Contains("state=Leased", reread);
            Assert.Contains("replay=30", reread);
            Assert.False(File.Exists(Path.Combine(root, "leases", "40.txt")));

            LoadedReplay second = await provider.TryLoadNextReplayAsync();
            Assert.Equal(20, second.ReplayId);
            Assert.Equal("2.55.17.98025", second.Replay.ReplayVersion);
            Assert.Equal(3, loader.Loads);
            string deferred = File.ReadAllText(Path.Combine(root, SpectateQueue.DeferredFileName));
            Assert.Contains("40 ", deferred);
            string spectated = File.ReadAllText(
                Path.Combine(root, SpectateQueue.SpectatedFileName)
            );
            Assert.DoesNotContain("40", spectated);
            Assert.DoesNotContain("20", spectated);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class VersionLoader : IReplayLoader
    {
        private readonly Dictionary<int, string> versions;

        public VersionLoader(Dictionary<int, string> versions)
        {
            this.versions = versions;
        }

        public int Loads { get; private set; }

        public Task<Replay> LoadAsync(string path)
        {
            Loads++;
            string name = Path.GetFileName(path);
            int id = int.Parse(name.Split('_')[0]);
            return Task.FromResult(new Replay { ReplayVersion = versions[id] });
        }
    }

    private sealed class IdleProfile : IHeroesProfileService
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
