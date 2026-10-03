using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MemoryCacheLookupTests
{
    [Fact]
    public async Task GetOrCreate_ReturnsTheStoredValueOnTheNextCall()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        int calls = 0;

        string first = await Read(
            cache,
            "tier",
            () =>
            {
                calls++;
                return "Diamond";
            }
        );
        string second = await Read(
            cache,
            "tier",
            () =>
            {
                calls++;
                return "Gold";
            }
        );

        Assert.Equal("Diamond", first);
        Assert.Equal("Diamond", second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetOrCreate_SkipsTheCacheWhenTheLifetimeIsZero()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        int calls = 0;

        string first = await MemoryCacheLookup.GetOrCreateAsync(
            cache,
            NullLogger.Instance,
            "empty",
            _ => TimeSpan.Zero,
            _ =>
            {
                calls++;
                return Task.FromResult("miss");
            },
            CancellationToken.None
        );
        string second = await MemoryCacheLookup.GetOrCreateAsync(
            cache,
            NullLogger.Instance,
            "empty",
            _ => TimeSpan.Zero,
            _ =>
            {
                calls++;
                return Task.FromResult("again");
            },
            CancellationToken.None
        );

        Assert.Equal("miss", first);
        Assert.Equal("again", second);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetOrCreate_CachesANullValue()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        int calls = 0;

        string first = await MemoryCacheLookup.GetOrCreateAsync<string>(
            cache,
            NullLogger.Instance,
            "replay",
            _ => TimeSpan.FromHours(2),
            _ =>
            {
                calls++;
                return Task.FromResult<string>(null);
            },
            CancellationToken.None
        );
        string second = await MemoryCacheLookup.GetOrCreateAsync<string>(
            cache,
            NullLogger.Instance,
            "replay",
            _ => TimeSpan.FromHours(2),
            _ =>
            {
                calls++;
                return Task.FromResult("later");
            },
            CancellationToken.None
        );

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetOrCreate_DoesNotCacheAFactoryException()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        int calls = 0;

        Task<string> Load() =>
            MemoryCacheLookup.GetOrCreateAsync<string>(
                cache,
                NullLogger.Instance,
                "broken",
                _ => TimeSpan.FromHours(1),
                _ =>
                {
                    calls++;
                    throw new InvalidOperationException("down");
                },
                CancellationToken.None
            );

        await Assert.ThrowsAsync<InvalidOperationException>(Load);
        await Assert.ThrowsAsync<InvalidOperationException>(Load);
        Assert.Equal(2, calls);
    }

    private static Task<string> Read(IMemoryCache cache, string key, Func<string> factory) =>
        MemoryCacheLookup.GetOrCreateAsync(
            cache,
            NullLogger.Instance,
            key,
            _ => TimeSpan.FromMinutes(5),
            _ => Task.FromResult(factory()),
            CancellationToken.None
        );
}
