using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Integration.HeroesProfile;

/// <summary>
/// #429, against the live Heroes Profile API: a rank reward queues a recent game whose tier on
/// its own ladder is the entered rank. The ranks are the common ones on each ladder (2026-10-13:
/// 11 of 25 Storm League games on Cursed Hollow were Bronze), so a search rarely comes back empty.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Integration)]
public class RankRewardLiveTests
{
    [Theory]
    [InlineData(GameType.StormLeague, "Cursed Hollow", GameRank.Bronze)]
    [InlineData(GameType.QuickMatch, "Hanamura Temple", GameRank.Gold)]
    [InlineData(GameType.ARAM, "Silver City", GameRank.Bronze)]
    public async Task RankReward_QueuesALiveGameAtTheEnteredRank(
        GameType gameType,
        string map,
        GameRank rank
    )
    {
        string apiKey = ResolveApiKey();
        Assert.False(
            string.IsNullOrWhiteSpace(apiKey),
            "Set HEROES_REPLAY_HeroesProfileApi__ApiKey or OP_SERVICE_ACCOUNT (`op read \"op://Heroes Replay/Heroes Profile API Key/password\"`)."
        );
        string directory = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-rank-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("HEROES_REPLAY_HeroesProfileApi__ApiKey", apiKey);
        using ServiceProvider provider = new ServiceCollection()
            .AddCheckServices(CancellationToken.None)
            .BuildServiceProvider();
        AppSettings settings = provider.GetRequiredService<AppSettings>();
        settings.Location.DataDirectory = directory;
        var lookup = provider.GetRequiredService<IReplayTierLookup>();
        using var queue = new RequestQueue(
            NullLogger<RequestQueue>.Instance,
            provider.GetRequiredService<IHeroesProfileService>(),
            settings,
            TimeSpan.FromSeconds(5),
            @"Local\HeroesReplay.RequestQueue.Test." + Guid.NewGuid().ToString("N"),
            @"Local\HeroesReplay.FailedRequests.Test." + Guid.NewGuid().ToString("N"),
            rewardsHolder: null,
            lookup
        );
        try
        {
            RewardResponse response = await queue.EnqueueItemAsync(
                new RewardRequest(
                    "integration",
                    Guid.Empty,
                    $"{map} (Rank)",
                    replayId: null,
                    rank,
                    map,
                    gameType
                )
                {
                    RankRequired = true,
                }
            );

            Assert.True(response.Success, response.Message);
            RewardQueueItem item = Assert.Single(
                RequestQueue.Snapshot(Path.Combine(directory, settings.Twitch.QueueFileName))
            );
            string tier = await lookup.GetTierAsync(
                item.HeroesProfileReplay.Id,
                gameType,
                CancellationToken.None
            );
            Assert.Equal(rank, RankInput.Read(tier));
            Assert.Contains($"({tier})", response.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ResolveApiKey()
    {
        string env = Environment.GetEnvironmentVariable("HEROES_REPLAY_HeroesProfileApi__ApiKey");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return SecretResolver.TryResolve(env);
        }

        return SecretResolver.TryResolve(SecretResolver.HeroesProfileApiKeyOpUri);
    }
}
