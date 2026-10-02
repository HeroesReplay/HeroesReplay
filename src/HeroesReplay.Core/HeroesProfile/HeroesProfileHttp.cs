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
                    ShouldRetryAfterHeader = false,
                    ShouldHandle = args => new ValueTask<bool>(
                        ShouldRetry(args.Outcome, args.Context.CancellationToken)
                    ),
                }
            )
            .AddTimeout(new HttpTimeoutStrategyOptions { Timeout = AttemptTimeout });
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

        int status = (int)response.StatusCode;
        return status == 429 || status >= (int)HttpStatusCode.InternalServerError;
    }
}
