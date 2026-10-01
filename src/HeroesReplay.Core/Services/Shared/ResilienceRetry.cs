using System;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;

namespace HeroesReplay.Core.Services.Shared;

internal static class ResilienceRetry
{
    public static TimeSpan ProcessKillDelay(int zeroBasedAttempt) =>
        TimeSpan.FromSeconds(Math.Pow(2, zeroBasedAttempt + 1));

    public static ResiliencePipeline<T> Constant<T>(
        int retries,
        TimeSpan delay,
        Func<Outcome<T>, bool> retry,
        Action<OnRetryArguments<T>> onRetry = null
    )
    {
        return Build(
            retries,
            DelayBackoffType.Constant,
            delay,
            retry,
            onRetry,
            delayForAttempt: null
        );
    }

    public static ResiliencePipeline<T> WithDelay<T>(
        int retries,
        Func<int, TimeSpan> delayForAttempt,
        Func<Outcome<T>, bool> retry,
        Action<OnRetryArguments<T>> onRetry = null
    )
    {
        if (delayForAttempt == null)
        {
            throw new ArgumentNullException(nameof(delayForAttempt));
        }

        return Build(
            retries,
            DelayBackoffType.Constant,
            TimeSpan.Zero,
            retry,
            onRetry,
            delayForAttempt
        );
    }

    public static bool Failed<T>(Outcome<T> outcome, Func<T, bool> resultFailed)
    {
        if (outcome.Exception is OperationCanceledException)
        {
            return false;
        }

        if (outcome.Exception != null)
        {
            return true;
        }

        return resultFailed(outcome.Result);
    }

    public static bool Failed<T>(
        Outcome<T> outcome,
        Func<Exception, bool> exceptionFailed,
        Func<T, bool> resultFailed
    )
    {
        if (outcome.Exception != null)
        {
            return exceptionFailed(outcome.Exception);
        }

        return resultFailed(outcome.Result);
    }

    private static ResiliencePipeline<T> Build<T>(
        int retries,
        DelayBackoffType backoff,
        TimeSpan delay,
        Func<Outcome<T>, bool> retry,
        Action<OnRetryArguments<T>> onRetry,
        Func<int, TimeSpan> delayForAttempt
    )
    {
        if (retry == null)
        {
            throw new ArgumentNullException(nameof(retry));
        }

        if (retries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retries));
        }

        // Polly requires at least one retry. Zero is the old single-attempt count.
        if (retries == 0)
        {
            return ResiliencePipeline<T>.Empty;
        }

        var options = new RetryStrategyOptions<T>
        {
            MaxRetryAttempts = retries,
            Delay = delay,
            BackoffType = backoff,
            UseJitter = false,
            ShouldHandle = args => new ValueTask<bool>(retry(args.Outcome)),
        };

        if (delayForAttempt != null)
        {
            options.DelayGenerator = args => new ValueTask<TimeSpan?>(
                delayForAttempt(args.AttemptNumber)
            );
        }

        if (onRetry != null)
        {
            options.OnRetry = args =>
            {
                onRetry(args);
                return default;
            };
        }

        return new ResiliencePipelineBuilder<T>().AddRetry(options).Build();
    }
}
