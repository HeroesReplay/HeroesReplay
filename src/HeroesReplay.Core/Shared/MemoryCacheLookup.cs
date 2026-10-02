using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Shared;

internal static class MemoryCacheLookup
{
    public static async Task<T> GetOrCreateAsync<T>(
        IMemoryCache cache,
        ILogger logger,
        string key,
        Func<T, TimeSpan> lifetime,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken
    )
    {
        if (cache == null)
        {
            throw new ArgumentNullException(nameof(cache));
        }

        if (logger == null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Cache key is required.", nameof(key));
        }

        if (lifetime == null)
        {
            throw new ArgumentNullException(nameof(lifetime));
        }

        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        try
        {
            if (cache.TryGetValue(key, out Box<T> hit))
            {
                logger.LogInformation("Cache Get for: {Key}", key);
                return hit.Value;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Cache Get error for: {Key}", key);
        }

        logger.LogInformation("Cache Miss for: {Key}", key);
        T value = await factory(cancellationToken).ConfigureAwait(false);
        TimeSpan ttl = lifetime(value);
        if (ttl <= TimeSpan.Zero)
        {
            return value;
        }

        try
        {
            cache.Set(key, new Box<T>(value), ttl);
            logger.LogInformation("Cache Put for: {Key}", key);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Cache Put error for: {Key}", key);
        }

        return value;
    }

    private sealed class Box<T>
    {
        public Box(T value)
        {
            Value = value;
        }

        public T Value { get; }
    }
}
