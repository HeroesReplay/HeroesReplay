using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Observer;
using HeroesReplay.Core.Services.Providers;
using HeroesReplay.Core.Services.Queue;
using HeroesReplay.Core.Services.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayQueuePickTests
{
    private static readonly string[] Installed = { "2.55.17.98025", "2.57.0.98304" };

    [Fact]
    public void CanLaunch_SkipsAMissingBuildAndKeepsAnInstalledOne()
    {
        Assert.False(ReplayQueuePick.CanLaunch("2.57.0.98285", Installed));
        Assert.False(ReplayQueuePick.CanLaunch("2.57.0.98297", Installed));
        Assert.True(ReplayQueuePick.CanLaunch("2.57.0.98304", Installed));
        Assert.True(ReplayQueuePick.CanLaunch("2.55.17.98025", Installed));
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
            Path.Combine(root, "Standard", "10_Storm League_Map_.StormReplay"),
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
                [10] = "2.57.0.98285",
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
            Assert.False(File.Exists(Path.Combine(root, "leases", "10.txt")));

            LoadedReplay second = await provider.TryLoadNextReplayAsync();
            Assert.Equal(20, second.ReplayId);
            Assert.Equal("2.55.17.98025", second.Replay.ReplayVersion);
            Assert.Equal(3, loader.Loads);
            string deferred = File.ReadAllText(Path.Combine(root, SpectateQueue.DeferredFileName));
            Assert.Contains("10 ", deferred);
            string spectated = File.ReadAllText(
                Path.Combine(root, SpectateQueue.SpectatedFileName)
            );
            Assert.DoesNotContain("10", spectated);
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
