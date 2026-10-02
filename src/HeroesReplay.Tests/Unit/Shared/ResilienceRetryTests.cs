using System;
using System.Net.Http;
using System.Threading.Tasks;
using HeroesReplay.Core.Shared;
using Polly;
using Xunit;

namespace HeroesReplay.Tests.Unit.Shared;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ResilienceRetryTests
{
    [Fact]
    public void ProcessKillDelay_DoublesFromTwoSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), ResilienceRetry.ProcessKillDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(4), ResilienceRetry.ProcessKillDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(8), ResilienceRetry.ProcessKillDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(16), ResilienceRetry.ProcessKillDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(32), ResilienceRetry.ProcessKillDelay(4));
    }

    [Fact]
    public async Task Constant_RetriesUntilTheResultSucceeds()
    {
        int calls = 0;
        bool result = await ResilienceRetry
            .Constant<bool>(
                retries: 3,
                delay: TimeSpan.Zero,
                retry: outcome => ResilienceRetry.Failed(outcome, value => value == false)
            )
            .ExecuteAsync(_ =>
            {
                calls++;
                return new ValueTask<bool>(calls >= 3);
            });

        Assert.True(result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Constant_DoesNotRetryWhenThePredicateRejectsTheException()
    {
        int calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ResilienceRetry
                .Constant<bool>(
                    retries: 5,
                    delay: TimeSpan.Zero,
                    retry: outcome => outcome.Exception is HttpRequestException
                )
                .ExecuteAsync(_ =>
                {
                    calls++;
                    return ValueTask.FromException<bool>(new InvalidOperationException("stop"));
                })
                .AsTask()
        );

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Failed_DoesNotRetryCancellation()
    {
        int calls = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ResilienceRetry
                .Constant<bool>(
                    retries: 4,
                    delay: TimeSpan.Zero,
                    retry: outcome => ResilienceRetry.Failed(outcome, _ => true)
                )
                .ExecuteAsync(_ =>
                {
                    calls++;
                    return ValueTask.FromException<bool>(new OperationCanceledException());
                })
                .AsTask()
        );

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Constant_ZeroRetriesRunsTheCallbackOnce()
    {
        int calls = 0;
        bool result = ResilienceRetry
            .Constant<bool>(retries: 0, delay: TimeSpan.Zero, retry: _ => true)
            .Execute(() =>
            {
                calls++;
                return false;
            });

        Assert.False(result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Constant_ZeroRetriesLetsTheCallbackExceptionEscape()
    {
        bool Fail() => throw new InvalidOperationException("once");

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            ResilienceRetry
                .Constant<bool>(retries: 0, delay: TimeSpan.Zero, retry: _ => true)
                .Execute(Fail)
        );

        Assert.Equal("once", thrown.Message);
    }

    [Fact]
    public void Constant_RejectsANegativeRetryCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ResilienceRetry.Constant<bool>(retries: -1, delay: TimeSpan.Zero, retry: _ => true)
        );
    }

    [Fact]
    public void Failed_RetriesANullResultAndAMatchingException()
    {
        Assert.True(
            ResilienceRetry.Failed<string>(Outcome.FromResult<string>(null), value => value == null)
        );
        Assert.False(ResilienceRetry.Failed(Outcome.FromResult("ok"), value => value == null));
        Assert.True(
            ResilienceRetry.Failed<bool>(
                Outcome.FromException<bool>(new InvalidOperationException()),
                error => error is InvalidOperationException,
                _ => false
            )
        );
        Assert.False(
            ResilienceRetry.Failed<bool>(
                Outcome.FromException<bool>(new HttpRequestException()),
                error => error is InvalidOperationException,
                _ => true
            )
        );
    }
}
