using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Telemetry;
using Polly.Timeout;

namespace HeroesReplay.Core.HeroesProfile;

public static class HeroesProfileHttp
{
    public const string ClientName = "heroes-profile";

    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(2);

    public const int MaxRetryAttempts = 10;

    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The longest <c>Retry-After</c> a 429 waits out. Heroes Profile limits requests per minute
    /// (<c>docs/heroesprofile-api.md</c>), so a longer wait is a quota, not that limit: that 429 is
    /// not retried and goes back to the caller, which skips the replay (#346).
    /// </summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(1);

    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> builder) =>
        Configure(builder, RetryDelay);

    public static void Configure(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        TimeSpan retryDelay
    )
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder
            .AddRetry(
                new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = MaxRetryAttempts,
                    Delay = retryDelay,
                    BackoffType = DelayBackoffType.Constant,
                    UseJitter = false,
                    // Setting this resets DelayGenerator, so it stays before it. Only a 429 waits
                    // for Retry-After (ShouldRetry hands one over MaxRetryAfter to the caller);
                    // everything else, and a 429 without the header, waits retryDelay.
                    ShouldRetryAfterHeader = false,
                    DelayGenerator = args => new ValueTask<TimeSpan?>(
                        args.Outcome.Result is HttpResponseMessage response
                            ? RetryAfter(response, DateTimeOffset.UtcNow)
                            : null
                    ),
                    ShouldHandle = args => new ValueTask<bool>(
                        ShouldRetry(args.Outcome, args.Context.CancellationToken)
                    ),
                }
            )
            .AddTimeout(new HttpTimeoutStrategyOptions { Timeout = AttemptTimeout });
    }

    /// <summary>
    /// How long a 429 asks to wait (<c>Retry-After</c>, as seconds or a date), never below zero.
    /// Null for any other answer, or a 429 without the header.
    /// </summary>
    public static TimeSpan? RetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        if (
            response is not { StatusCode: HttpStatusCode.TooManyRequests }
            || response.Headers.RetryAfter is not { } header
        )
        {
            return null;
        }

        TimeSpan? wait = header.Delta ?? (header.Date - now);
        return wait is TimeSpan value && value < TimeSpan.Zero ? TimeSpan.Zero : wait;
    }

    /// <summary>
    /// A request cancelled by a stop is not a failure. Its attempt is logged at Debug,
    /// not as an Information line with a stack trace.
    /// </summary>
    public static ResilienceEventSeverity Severity(SeverityProviderArguments args) =>
        args.Context?.CancellationToken.IsCancellationRequested == true
            ? ResilienceEventSeverity.Debug
            : args.Event.Severity;

    public static bool ShouldRetry(
        Outcome<HttpResponseMessage> outcome,
        CancellationToken cancellationToken
    )
    {
        if (
            outcome.Exception is OperationCanceledException
            && cancellationToken.IsCancellationRequested
        )
        {
            return false;
        }

        if (outcome.Exception is HttpRequestException or TimeoutRejectedException)
        {
            return true;
        }

        if (
            outcome.Exception is OperationCanceledException { InnerException: TimeoutException }
            && !cancellationToken.IsCancellationRequested
        )
        {
            return true;
        }

        if (outcome.Result is not HttpResponseMessage response)
        {
            return false;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // A 429 without Retry-After waits RetryDelay; one that asks for longer than
            // MaxRetryAfter is a quota, so the caller gets it now.
            return RetryAfter(response, DateTimeOffset.UtcNow) is not TimeSpan wait
                || wait <= MaxRetryAfter;
        }

        return (int)response.StatusCode >= (int)HttpStatusCode.InternalServerError;
    }
}
