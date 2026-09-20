using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.CLI;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroesReplay.Tests.Integration.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Integration)]
public class HeroesProfileApiTests
{
    [Fact]
    public async Task GetMaxReplayId_ReturnsLiveId()
    {
        string apiKey = ResolveApiKey();
        Assert.False(
            string.IsNullOrWhiteSpace(apiKey),
            "Set HEROES_REPLAY_HeroesProfileApi__ApiKey or OP_SERVICE_ACCOUNT (`op read \"op://Heroes Replay/Heroes Profile API Key/password\"`)."
        );

        using var provider = CreateProvider(apiKey);
        IHeroesProfileService api = provider.GetRequiredService<IHeroesProfileService>();
        AppSettings settings = provider.GetRequiredService<AppSettings>();

        int maxId = await api.GetMaxReplayIdAsync();

        Assert.True(maxId > 0, $"Expected a positive replay id, got {maxId}.");
        Assert.NotEqual(settings.HeroesProfileApi.FallbackMaxReplayId, maxId);
    }

    [Fact]
    public async Task ListDownloadParse_LoadsAStormLeagueReplay()
    {
        string apiKey = ResolveApiKey();
        Assert.False(
            string.IsNullOrWhiteSpace(apiKey),
            "Set HEROES_REPLAY_HeroesProfileApi__ApiKey or OP_SERVICE_ACCOUNT (`op read \"op://Heroes Replay/Heroes Profile API Key/password\"`)."
        );

        using var provider = CreateProvider(apiKey);
        IHeroesProfileService api = provider.GetRequiredService<IHeroesProfileService>();
        AppSettings settings = provider.GetRequiredService<AppSettings>();

        int maxId = await api.GetMaxReplayIdAsync();
        Assert.True(maxId > 0, $"Expected a positive replay id, got {maxId}.");
        Assert.NotEqual(settings.HeroesProfileApi.FallbackMaxReplayId, maxId);

        HeroesProfileReplay found = null;
        int minId = Math.Max(
            settings.HeroesProfileApi.MinReplayId,
            maxId - settings.HeroesProfileApi.ApiMaxReturnedReplays
        );
        for (int attempt = 0; attempt < 8 && found == null; attempt++)
        {
            var page = (await api.GetReplaysByMinId(minId)).OrderBy(r => r.Id).ToList();
            found = page.FirstOrDefault(r => r.Id > minId);
            if (found == null && page.Count > 0)
            {
                minId = page.Max(r => r.Id);
            }
            else if (found == null)
            {
                minId = Math.Max(
                    settings.HeroesProfileApi.MinReplayId,
                    minId - settings.HeroesProfileApi.ApiMaxReturnedReplays
                );
            }
        }

        Assert.NotNull(found);
        Assert.True(found.Id > 0);
        Assert.True(settings.HeroesProfileApi.IsAllowedGameType(found.GameType), found.GameType);
        Assert.False(string.IsNullOrWhiteSpace(found.Map));

        string path = Path.Combine(Path.GetTempPath(), $"heroesreplay-hp-{found.Id}.StormReplay");
        try
        {
            await using (FileStream file = File.Create(path))
            {
                await api.DownloadReplayAsync(found.Id, file, CancellationToken.None);
            }

            var info = new FileInfo(path);
            Assert.True(info.Length > 100_000, $"Downloaded {info.Length} bytes for {found.Id}.");

            (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(
                await File.ReadAllBytesAsync(path),
                new ParseOptions
                {
                    ShouldParseEvents = true,
                    ShouldParseUnits = true,
                    ShouldParseStatistics = true,
                }
            );

            Assert.True(
                result == DataParser.ReplayParseResult.Success
                    || result == DataParser.ReplayParseResult.UnexpectedResult,
                $"Parse {found.Id}: {result}"
            );
            Assert.NotNull(replay);
            Assert.False(string.IsNullOrWhiteSpace(replay.Map));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
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

    private static ServiceProvider CreateProvider(string apiKey)
    {
        Environment.SetEnvironmentVariable("HEROES_REPLAY_HeroesProfileApi__ApiKey", apiKey);
        return new ServiceCollection()
            .AddCheckServices(CancellationToken.None)
            .BuildServiceProvider();
    }
}
