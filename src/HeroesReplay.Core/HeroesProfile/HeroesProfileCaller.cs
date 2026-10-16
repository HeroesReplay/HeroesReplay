using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// The paced GET behind the statistics calls and the player lookups (#433). Each request waits
/// its turn on the shared <see cref="HeroesProfilePacer"/>. A 202 is collected from
/// <c>/jobs/{id}</c> (polling costs no quota), a 429 waits for <c>Retry-After</c> (60 s without
/// one, at most 5 times), and a 5xx or a network error is tried again 30 s later, 3 times. The
/// answer is the 200 with its body, or the error answer for the caller to judge; a
/// <see cref="HeroStatsException"/> means retries, polls or the job's time ran out.
/// </summary>
public sealed class HeroesProfileCaller
{
    /// <summary>The wait between job polls when a 202 has no <c>Retry-After</c>.</summary>
    public static readonly TimeSpan DefaultPollDelay = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan DefaultRateLimitDelay = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan TransientDelay = TimeSpan.FromSeconds(30);

    public const int MaxRateLimited = 5;

    public const int MaxTransient = 3;

    private readonly IHeroStatsApi api;
    private readonly HeroesProfilePacer pacer;
    private readonly ILogger logger;
    private readonly Func<DateTimeOffset> clock;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public HeroesProfileCaller(
        IHeroStatsApi api,
        HeroesProfilePacer pacer,
        ILogger logger,
        Func<DateTimeOffset> clock = null,
        Func<TimeSpan, CancellationToken, Task> delay = null
    )
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.pacer = pacer ?? throw new ArgumentNullException(nameof(pacer));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.delay = delay ?? ((wait, token) => Task.Delay(wait, token));
    }

    public async Task<HeroesProfileGlobalAnswer> GetAsync(
        string path,
        IReadOnlyList<KeyValuePair<string, string>> query,
        TimeSpan jobTimeout,
        CancellationToken cancellationToken,
        string lane = null,
        TimeSpan laneGap = default
    )
    {
        int limited = 0;
        int transient = 0;
        while (true)
        {
            await pacer.WaitAsync(cancellationToken, lane, laneGap).ConfigureAwait(false);
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

            if (answer.StatusCode == 202)
            {
                return await CollectAsync(path, answer, jobTimeout, cancellationToken)
                    .ConfigureAwait(false);
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

            return answer;
        }
    }

    /// <summary>The exception for an answer that is not a 200.</summary>
    public static HeroStatsException Failed(string path, HeroesProfileGlobalAnswer answer)
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

    /// <summary>Polls the job a 202 named until it answers, fails, or runs past <paramref name="jobTimeout"/>.</summary>
    private async Task<HeroesProfileGlobalAnswer> CollectAsync(
        string path,
        HeroesProfileGlobalAnswer accepted,
        TimeSpan jobTimeout,
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

        TimeSpan timeout = jobTimeout > TimeSpan.Zero ? jobTimeout : TimeSpan.FromMinutes(15);
        DateTimeOffset deadline = clock() + timeout;
        TimeSpan wait = accepted.RetryAfter ?? DefaultPollDelay;
        int transient = 0;
        while (true)
        {
            await delay(wait, cancellationToken).ConfigureAwait(false);
            if (clock() > deadline)
            {
                throw new HeroStatsException(
                    HeroStatsFailure.Unavailable,
                    $"Heroes Profile job {accepted.JobId} for {path} did not finish within {timeout}."
                );
            }

            await pacer.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                case 202:
                    wait = answer.RetryAfter ?? DefaultPollDelay;
                    continue;
                case 429:
                    wait = answer.RetryAfter ?? DefaultRateLimitDelay;
                    continue;
                default:
                    return answer;
            }
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

    private static bool IsTransient(Exception e, CancellationToken cancellationToken) =>
        e is HttpRequestException or IOException
        || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}
