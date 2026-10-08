using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// Keeps <see cref="HeroStatsStore"/> current for the newest major patch, one file per configured
/// game type, from three Heroes Profile calls: <c>/heroes</c> (ids and <c>attribute_id</c>),
/// one <c>/heroes/stats?group_by_map=true</c> (every hero on every map), and one
/// <c>/heroes/matchups</c> per hero. A 202 is collected from <c>/jobs/{id}</c>, a 429 waits for
/// <c>Retry-After</c>, and requests are spaced under the per-minute limit. 401 or 403 stops the
/// refresh until a restart, with one warning; 422 skips that patch and game type until the next
/// refresh is due. Only the download role runs this; spectate only reads the files.
/// </summary>
public sealed class HeroStatsRefresh
{
    /// <summary>The wait between job polls when a 202 has no <c>Retry-After</c>.</summary>
    public static readonly TimeSpan DefaultPollDelay = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan DefaultRateLimitDelay = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan TransientDelay = TimeSpan.FromSeconds(30);

    public const int MaxRateLimited = 5;

    public const int MaxTransient = 3;

    private readonly IHeroStatsApi api;
    private readonly HeroStatsStore store;
    private readonly HeroStatsSettings settings;
    private readonly ILogger<HeroStatsRefresh> logger;
    private readonly Func<DateTimeOffset> clock;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Dictionary<string, DateTimeOffset> rejectedUntil = new Dictionary<
        string,
        DateTimeOffset
    >(StringComparer.Ordinal);

    private DateTimeOffset? lastRequest;
    private DateTimeOffset? lastGroupByMap;

    public HeroStatsRefresh(
        IHeroStatsApi api,
        HeroStatsStore store,
        HeroStatsSettings settings,
        ILogger<HeroStatsRefresh> logger,
        Func<DateTimeOffset> clock = null,
        Func<TimeSpan, CancellationToken, Task> delay = null
    )
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.settings = settings ?? new HeroStatsSettings();
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.delay = delay ?? ((wait, token) => Task.Delay(wait, token));
    }

    /// <summary>True after a 401 or 403. Nothing more is fetched by this instance.</summary>
    public bool Stopped { get; private set; }

    public HeroStatsStore Store => store;

    /// <summary>Checks every <c>CheckInterval</c> and refreshes what is due, until cancelled or stopped.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Hero statistics refresh is on for {GameTypes}: every {RefreshInterval} into {Folder}.",
            string.Join(", ", GameTypeCodes()),
            settings.RefreshInterval,
            store.Folder
        );
        while (!cancellationToken.IsCancellationRequested && !Stopped)
        {
            try
            {
                await RefreshDueAsync(force: false, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    e,
                    "Hero statistics refresh failed. It runs again in {CheckInterval}; titles keep their usual form.",
                    settings.CheckInterval
                );
            }

            if (Stopped)
            {
                break;
            }

            try
            {
                await delay(
                        Positive(settings.CheckInterval, TimeSpan.FromHours(1)),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// One pass over the newest major patch: each configured game type whose file is due, or every
    /// one when <paramref name="force"/> is set. Old patches are pruned, keeping the newest two.
    /// Returns the files written.
    /// </summary>
    public async Task<IReadOnlyList<string>> RefreshDueAsync(
        bool force,
        CancellationToken cancellationToken
    )
    {
        var written = new List<string>();
        if (Stopped)
        {
            return written;
        }

        try
        {
            IReadOnlyList<string> majors = HeroStatsJson.ReadMajorPatches(
                await FetchAsync("patches", null, groupByMap: false, cancellationToken)
                    .ConfigureAwait(false)
            );
            if (majors.Count == 0)
            {
                logger.LogWarning(
                    "Heroes Profile listed no patch with global statistics. Hero statistics were not refreshed."
                );
                return written;
            }

            string patch = majors[0];
            foreach (string code in GameTypeCodes())
            {
                string key = patch + "-" + code;
                DateTimeOffset now = clock();
                if (
                    !force
                    && rejectedUntil.TryGetValue(key, out DateTimeOffset until)
                    && now < until
                )
                {
                    continue;
                }

                if (!force && !store.IsDue(patch, code, now, settings.RefreshInterval))
                {
                    continue;
                }

                try
                {
                    HeroStatsSnapshot snapshot = await RefreshAsync(patch, code, cancellationToken)
                        .ConfigureAwait(false);
                    written.Add(store.Write(snapshot));
                    rejectedUntil.Remove(key);
                }
                catch (HeroStatsException e) when (e.Failure == HeroStatsFailure.Rejected)
                {
                    rejectedUntil[key] =
                        clock() + Positive(settings.RefreshInterval, TimeSpan.FromHours(24));
                    logger.LogWarning(
                        "Heroes Profile refused hero statistics for patch {Patch} {GameType} ({StatusCode} {ErrorCode}). Not asked again for {RefreshInterval}.",
                        patch,
                        code,
                        e.StatusCode,
                        e.ErrorCode,
                        settings.RefreshInterval
                    );
                }
            }

            IReadOnlyList<string> deleted = store.Prune(majors.Take(2));
            if (deleted.Count > 0)
            {
                logger.LogInformation(
                    "Deleted hero statistics of older patches: {Files}.",
                    string.Join(", ", deleted)
                );
            }
        }
        catch (HeroStatsException e) when (e.Failure == HeroStatsFailure.AccessDenied)
        {
            Stopped = true;
            logger.LogWarning(
                "Heroes Profile refused the hero statistics calls ({StatusCode} {ErrorCode}). The refresh stops until the download role restarts; titles keep their usual form.",
                e.StatusCode,
                e.ErrorCode
            );
        }

        return written;
    }

    /// <summary>Fetches one patch and game type. Throws <see cref="HeroStatsException"/>.</summary>
    public async Task<HeroStatsSnapshot> RefreshAsync(
        string patch,
        string gameTypeCode,
        CancellationToken cancellationToken
    )
    {
        string major =
            HeroStatsPatch.Major(patch)
            ?? throw new ArgumentException(
                "A major patch such as 2.57 is required.",
                nameof(patch)
            );
        string code =
            HeroStatsPatch.GameTypeCode(gameTypeCode)
            ?? throw new ArgumentException("A game type is required.", nameof(gameTypeCode));
        var watch = Stopwatch.StartNew();

        IReadOnlyList<HeroStatsHeroRef> heroes = HeroStatsJson.ReadHeroes(
            await FetchAsync("heroes", null, groupByMap: false, cancellationToken)
                .ConfigureAwait(false)
        );
        if (heroes.Count == 0)
        {
            throw new HeroStatsException(
                HeroStatsFailure.Unavailable,
                "Heroes Profile listed no heroes."
            );
        }

        var timeframe = new List<KeyValuePair<string, string>>
        {
            new("timeframe_type", "major"),
            new("timeframe", major),
            new("game_type", code),
        };
        Dictionary<int, List<HeroMapStats>> maps = HeroStatsJson.ReadMapStats(
            await FetchAsync(
                    "heroes/stats",
                    timeframe
                        .Append(new KeyValuePair<string, string>("group_by_map", "true"))
                        .ToList(),
                    groupByMap: true,
                    cancellationToken
                )
                .ConfigureAwait(false)
        );
        if (maps.Count == 0)
        {
            throw new HeroStatsException(
                HeroStatsFailure.Unavailable,
                $"Heroes Profile has no map statistics for patch {major} {code}."
            );
        }

        var snapshot = new HeroStatsSnapshot { Patch = major, GameType = code };
        int withMatchups = 0;
        foreach (HeroStatsHeroRef hero in heroes.OrderBy(hero => hero.Name, StringComparer.Ordinal))
        {
            if (!maps.TryGetValue(hero.Id, out List<HeroMapStats> rows) || rows.Count == 0)
            {
                continue;
            }

            var stats = new HeroStats
            {
                AttributeId = hero.AttributeId,
                Name = hero.Name,
                Maps = rows.OrderBy(row => row.Map, StringComparer.Ordinal).ToList(),
                Wins = rows.Sum(row => row.Wins),
                Games = rows.Sum(row => row.Games),
            };
            try
            {
                HeroMatchups matchups = HeroStatsJson.ReadMatchups(
                    await FetchAsync(
                            "heroes/matchups",
                            timeframe
                                .Append(new KeyValuePair<string, string>("hero", hero.Name))
                                .ToList(),
                            groupByMap: false,
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                );
                stats.Enemies = matchups.Enemies;
                stats.Allies = matchups.Allies;
                if (matchups.Enemies.Count > 0 || matchups.Allies.Count > 0)
                {
                    withMatchups++;
                }
            }
            catch (HeroStatsException e) when (e.Failure == HeroStatsFailure.Unavailable)
            {
                logger.LogWarning(
                    "No matchups for {Hero} on patch {Patch} {GameType}: {Reason}",
                    hero.Name,
                    major,
                    code,
                    e.Message
                );
            }

            snapshot.Heroes.Add(stats);
        }

        snapshot.FetchedAtUtc = clock();
        logger.LogInformation(
            "Hero statistics for patch {Patch} {GameType}: {Heroes} heroes, {WithMatchups} with matchups, in {Elapsed}.",
            major,
            code,
            snapshot.Heroes.Count,
            withMatchups,
            watch.Elapsed
        );
        return snapshot;
    }

    private IEnumerable<string> GameTypeCodes() =>
        settings
            .GameTypeList()
            .Select(HeroStatsPatch.GameTypeCode)
            .Where(code => code != null)
            .Distinct(StringComparer.Ordinal);

    private async Task<string> FetchAsync(
        string path,
        IReadOnlyList<KeyValuePair<string, string>> query,
        bool groupByMap,
        CancellationToken cancellationToken
    )
    {
        int limited = 0;
        int transient = 0;
        while (true)
        {
            await PaceAsync(groupByMap, cancellationToken).ConfigureAwait(false);
            HeroesProfileGlobalAnswer answer;
            try
            {
                answer = await api.GetAsync(path, query, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (IsTransient(e, cancellationToken))
            {
                await TransientAsync(path, ++transient, e, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (answer.StatusCode == 200)
            {
                return answer.Body;
            }

            if (answer.StatusCode == 202)
            {
                return await CollectAsync(path, answer, cancellationToken).ConfigureAwait(false);
            }

            if (answer.StatusCode == 429)
            {
                if (++limited > MaxRateLimited)
                {
                    throw Failed(path, answer);
                }

                await delay(answer.RetryAfter ?? DefaultRateLimitDelay, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            if (answer.StatusCode >= 500)
            {
                await TransientAsync(path, ++transient, Failed(path, answer), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            throw Failed(path, answer);
        }
    }

    /// <summary>Polls the job a 202 named until it answers 200, fails, or runs past <c>JobTimeout</c>.</summary>
    private async Task<string> CollectAsync(
        string path,
        HeroesProfileGlobalAnswer accepted,
        CancellationToken cancellationToken
    )
    {
        if (accepted.JobId == null)
        {
            throw new HeroStatsException(
                HeroStatsFailure.Unavailable,
                $"Heroes Profile accepted {path} without a job id.",
                accepted.StatusCode
            );
        }

        DateTimeOffset deadline = clock() + Positive(settings.JobTimeout, TimeSpan.FromMinutes(15));
        TimeSpan wait = accepted.RetryAfter ?? DefaultPollDelay;
        int transient = 0;
        while (true)
        {
            await delay(wait, cancellationToken).ConfigureAwait(false);
            if (clock() > deadline)
            {
                throw new HeroStatsException(
                    HeroStatsFailure.Unavailable,
                    $"Heroes Profile job {accepted.JobId} for {path} did not finish within {settings.JobTimeout}."
                );
            }

            await PaceAsync(groupByMap: false, cancellationToken).ConfigureAwait(false);
            HeroesProfileGlobalAnswer answer;
            try
            {
                answer = await api.GetJobAsync(accepted.JobId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (IsTransient(e, cancellationToken))
            {
                if (++transient > MaxTransient)
                {
                    throw new HeroStatsException(
                        HeroStatsFailure.Unavailable,
                        $"Could not poll Heroes Profile job {accepted.JobId} for {path}.",
                        inner: e
                    );
                }

                wait = TransientDelay;
                continue;
            }

            switch (answer.StatusCode)
            {
                case 200:
                    return answer.Body;
                case 202:
                    wait = answer.RetryAfter ?? DefaultPollDelay;
                    continue;
                case 429:
                    wait = answer.RetryAfter ?? DefaultRateLimitDelay;
                    continue;
                default:
                    throw Failed("jobs/" + accepted.JobId + " (" + path + ")", answer);
            }
        }
    }

    /// <summary>Waits until the next request keeps under the per-minute and <c>group_by_map</c> limits.</summary>
    private async Task PaceAsync(bool groupByMap, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock();
        TimeSpan wait = TimeSpan.Zero;
        if (lastRequest is DateTimeOffset last)
        {
            wait = Max(wait, last + settings.RequestSpacing - now);
        }

        if (groupByMap && lastGroupByMap is DateTimeOffset lastBatch)
        {
            wait = Max(wait, lastBatch + settings.GroupByMapSpacing - now);
        }

        if (wait > TimeSpan.Zero)
        {
            await delay(wait, cancellationToken).ConfigureAwait(false);
        }

        lastRequest = clock();
        if (groupByMap)
        {
            lastGroupByMap = lastRequest;
        }
    }

    private async Task TransientAsync(
        string path,
        int attempt,
        Exception failure,
        CancellationToken cancellationToken
    )
    {
        if (attempt > MaxTransient)
        {
            throw failure as HeroStatsException
                ?? new HeroStatsException(
                    HeroStatsFailure.Unavailable,
                    $"Could not reach Heroes Profile for {path}.",
                    inner: failure
                );
        }

        logger.LogInformation(
            "Heroes Profile {Path} failed ({Reason}); trying again in {Delay}.",
            path,
            failure.Message,
            TransientDelay
        );
        await delay(TransientDelay, cancellationToken).ConfigureAwait(false);
    }

    private static HeroStatsException Failed(string path, HeroesProfileGlobalAnswer answer)
    {
        HeroStatsFailure failure = answer.StatusCode switch
        {
            401 or 403 => HeroStatsFailure.AccessDenied,
            422 => HeroStatsFailure.Rejected,
            _ => HeroStatsFailure.Unavailable,
        };
        return new HeroStatsException(
            failure,
            $"Heroes Profile {path} answered {answer.StatusCode} {answer.ErrorCode}".TrimEnd()
                + ".",
            answer.StatusCode,
            answer.ErrorCode
        );
    }

    private static bool IsTransient(Exception e, CancellationToken cancellationToken) =>
        e is HttpRequestException or IOException
        || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static TimeSpan Positive(TimeSpan value, TimeSpan fallback) =>
        value > TimeSpan.Zero ? value : fallback;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
