using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Telemetry;
using Polly.Timeout;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesProfileHttpTests
{
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData((HttpStatusCode)429, true)]
    [InlineData(HttpStatusCode.RequestTimeout, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.OK, false)]
    public void ShouldRetry_MatchesTransientHeroesProfileStatuses(HttpStatusCode status, bool retry)
    {
        using var response = new HttpResponseMessage(status);
        Assert.Equal(
            retry,
            HeroesProfileHttp.ShouldRetry(Outcome.FromResult(response), CancellationToken.None)
        );
    }

    [Fact]
    public void ShouldRetry_RetriesTransportFailuresAndStopsWhenCancelled()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.True(
            HeroesProfileHttp.ShouldRetry(
                Outcome.FromException<HttpResponseMessage>(new HttpRequestException("down")),
                CancellationToken.None
            )
        );
        Assert.True(
            HeroesProfileHttp.ShouldRetry(
                Outcome.FromException<HttpResponseMessage>(new TimeoutRejectedException()),
                CancellationToken.None
            )
        );
        Assert.False(
            HeroesProfileHttp.ShouldRetry(
                Outcome.FromException<HttpResponseMessage>(new OperationCanceledException()),
                cancelled.Token
            )
        );
        Assert.False(
            HeroesProfileHttp.ShouldRetry(
                Outcome.FromException<HttpResponseMessage>(new InvalidOperationException("parse")),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public void Severity_LogsACancelledAttemptAtDebug()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var source = new ResilienceTelemetrySource("heroes-profile", null, "Retry");
        var attempt = new ResilienceEvent(ResilienceEventSeverity.Information, "ExecutionAttempt");
        ResilienceContext stopped = ResilienceContextPool.Shared.Get(cancelled.Token);
        ResilienceContext running = ResilienceContextPool.Shared.Get(CancellationToken.None);
        try
        {
            Assert.Equal(
                ResilienceEventSeverity.Debug,
                HeroesProfileHttp.Severity(new SeverityProviderArguments(source, attempt, stopped))
            );
            Assert.Equal(
                ResilienceEventSeverity.Information,
                HeroesProfileHttp.Severity(new SeverityProviderArguments(source, attempt, running))
            );
        }
        finally
        {
            ResilienceContextPool.Shared.Return(stopped);
            ResilienceContextPool.Shared.Return(running);
        }
    }

    [Fact]
    public async Task Handler_RetriesServerErrorsThenReturnsTheSuccess()
    {
        var handler = new ScriptedHandler(call =>
            call < 3 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK
        );

        using ServiceProvider provider = CreateProvider(handler);
        HttpClient client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(HeroesProfileHttp.ClientName);

        using HttpResponseMessage response = await client.GetAsync(
            "https://heroesprofile.test/replays"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Handler_DoesNotRetryNotFound()
    {
        var handler = new ScriptedHandler(_ => HttpStatusCode.NotFound);

        using ServiceProvider provider = CreateProvider(handler);
        HttpClient client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(HeroesProfileHttp.ClientName);

        using HttpResponseMessage response = await client.GetAsync(
            "https://heroesprofile.test/missing"
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RetryAfter_ReadsA429sSecondsOrDate()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(30),
            HeroesProfileHttp.RetryAfter(RateLimited(TimeSpan.FromSeconds(30)), Now)
        );
        Assert.Equal(
            TimeSpan.FromSeconds(20),
            HeroesProfileHttp.RetryAfter(RateLimited(Now.AddSeconds(20)), Now)
        );
        Assert.Equal(
            TimeSpan.Zero,
            HeroesProfileHttp.RetryAfter(RateLimited(Now.AddSeconds(-5)), Now)
        );

        using var bare = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        Assert.Null(HeroesProfileHttp.RetryAfter(bare, Now));
        using var unavailable = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        unavailable.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        Assert.Null(HeroesProfileHttp.RetryAfter(unavailable, Now));
    }

    /// <summary>#346: the per-minute limit is waited out; a longer wait is a quota, so the caller skips.</summary>
    [Fact]
    public void ShouldRetry_A429AskingForMoreThanAMinute_GoesBackToTheCaller()
    {
        using HttpResponseMessage minute = RateLimited(HeroesProfileHttp.MaxRetryAfter);
        using HttpResponseMessage longer = RateLimited(TimeSpan.FromMinutes(5));

        Assert.True(
            HeroesProfileHttp.ShouldRetry(Outcome.FromResult(minute), CancellationToken.None)
        );
        Assert.False(
            HeroesProfileHttp.ShouldRetry(Outcome.FromResult(longer), CancellationToken.None)
        );
    }

    /// <summary>#346: a 429 waits for its Retry-After, not the constant delay (an hour here).</summary>
    [Fact]
    public async Task Handler_A429WaitsForItsRetryAfterThenReturnsTheSuccess()
    {
        var handler = new ScriptedHandler(call =>
            call == 1 ? RateLimited(TimeSpan.Zero) : new HttpResponseMessage(HttpStatusCode.OK)
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        using ServiceProvider provider = CreateProvider(handler, TimeSpan.FromHours(1));
        HttpClient client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(HeroesProfileHttp.ClientName);

        using HttpResponseMessage response = await client.GetAsync(
            "https://heroesprofile.test/download/replay?replayID=1",
            timeout.Token
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Handler_A429WithAQuotaRetryAfter_IsReturnedWithoutARetry()
    {
        var handler = new ScriptedHandler(_ => RateLimited(TimeSpan.FromHours(1)));

        using ServiceProvider provider = CreateProvider(handler);
        HttpClient client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(HeroesProfileHttp.ClientName);

        using HttpResponseMessage response = await client.GetAsync(
            "https://heroesprofile.test/download/replay?replayID=1"
        );

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    private static HttpResponseMessage RateLimited(TimeSpan delay)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
        return response;
    }

    private static HttpResponseMessage RateLimited(DateTimeOffset date)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(date);
        return response;
    }

    private static ServiceProvider CreateProvider(
        HttpMessageHandler handler,
        TimeSpan? retryDelay = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddHttpClient(
                HeroesProfileHttp.ClientName,
                client => client.Timeout = Timeout.InfiniteTimeSpan
            )
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler(
                "heroes-profile-test",
                builder => HeroesProfileHttp.Configure(builder, retryDelay ?? TimeSpan.Zero)
            );
        return services.BuildServiceProvider();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> respond;

        public ScriptedHandler(Func<int, HttpStatusCode> status)
            : this(call => new HttpResponseMessage(status(call))) { }

        public ScriptedHandler(Func<int, HttpResponseMessage> respond)
        {
            this.respond = respond;
        }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(Calls));
        }
    }
}
