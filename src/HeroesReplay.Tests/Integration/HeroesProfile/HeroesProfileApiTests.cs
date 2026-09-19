using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.Core.Configuration;
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
            "Set HEROES_REPLAY_HeroesProfileApi__ApiKey or sign in with 1Password CLI (`op read \"op://Private/Heroes Profile API/password\"`)."
        );

        using var provider = CreateProvider(apiKey);
        IHeroesProfileService api = provider.GetRequiredService<IHeroesProfileService>();
        AppSettings settings = provider.GetRequiredService<AppSettings>();

        int maxId = await api.GetMaxReplayIdAsync();

        Assert.True(maxId > 0, $"Expected a positive replay id, got {maxId}.");
        Assert.NotEqual(settings.HeroesProfileApi.FallbackMaxReplayId, maxId);
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
