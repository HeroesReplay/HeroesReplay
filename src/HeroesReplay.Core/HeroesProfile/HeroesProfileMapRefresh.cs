using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// Fetches Heroes Profile's <c>/maps</c> before a reward sync when the saved list is older than
/// <c>HeroesProfileApi:MapsRefreshInterval</c> (#425). The Storm League pool changes once a
/// season, so a saved list is reused until it is due. A failed call keeps the saved list and
/// never stops the sync.
/// </summary>
public sealed class HeroesProfileMapRefresh
{
    private readonly IHeroStatsApi api;
    private readonly HeroesProfileMapStore store;
    private readonly TimeSpan refreshInterval;
    private readonly ILogger<HeroesProfileMapRefresh> logger;
    private readonly Func<DateTimeOffset> clock;

    public HeroesProfileMapRefresh(
        IHeroStatsApi api,
        HeroesProfileMapStore store,
        TimeSpan refreshInterval,
        ILogger<HeroesProfileMapRefresh> logger,
        Func<DateTimeOffset> clock = null
    )
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.refreshInterval = refreshInterval;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The list the pool should use: fetched now when due, otherwise the saved one (or null).</summary>
    public async Task<HeroesProfileMapList> RefreshIfDueAsync(CancellationToken cancellationToken)
    {
        HeroesProfileMapList saved = store.Read();
        DateTimeOffset now = clock();
        if (
            store.FilePath == null
            || refreshInterval <= TimeSpan.Zero
            || HeroesProfileMapStore.IsFresh(saved, now, refreshInterval)
        )
        {
            return saved;
        }

        HeroesProfileGlobalAnswer answer;
        try
        {
            answer = await api.GetAsync(
                    "maps",
                    Array.Empty<KeyValuePair<string, string>>(),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return saved;
        }
        catch (Exception e)
        {
            logger.LogWarning(
                "Could not fetch the Heroes Profile map list: {Error}. {Kept}",
                e.Message,
                Kept(saved)
            );
            return saved;
        }

        if (answer.StatusCode != 200)
        {
            logger.LogWarning(
                "Heroes Profile answered the map list with HTTP {Status} {Code}. {Kept}",
                answer.StatusCode,
                answer.ErrorCode ?? string.Empty,
                Kept(saved)
            );
            return saved;
        }

        HeroesProfileMapList fetched = HeroesProfileMapList.FromJson(answer.Body, now);
        if (fetched == null)
        {
            logger.LogWarning("Heroes Profile's map list could not be read. {Kept}", Kept(saved));
            return saved;
        }

        try
        {
            store.Write(fetched);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                "Could not save the Heroes Profile map list to {Path}: {Error}",
                store.FilePath,
                e.Message
            );
        }

        logger.LogInformation(
            "Heroes Profile map list fetched. Ranked rotation: {Ranked}.",
            string.Join(", ", fetched.RankedNames.OrderBy(name => name, StringComparer.Ordinal))
        );
        return fetched;
    }

    private static string Kept(HeroesProfileMapList saved) =>
        saved == null
            ? "No saved list: the cached replays and Maps:Catalog decide the pool."
            : $"Keeping the list fetched {saved.FetchedAtUtc:yyyy-MM-dd HH:mm} UTC.";
}
