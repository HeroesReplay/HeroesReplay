using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Services.HeroesProfile;
using Microsoft.Extensions.DependencyInjection;
using Polly;
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

    private static ServiceProvider CreateProvider(HttpMessageHandler handler)
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
                builder => HeroesProfileHttp.Configure(builder, TimeSpan.Zero)
            );
        return services.BuildServiceProvider();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpStatusCode> status;

        public ScriptedHandler(Func<int, HttpStatusCode> status)
        {
            this.status = status;
        }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status(Calls)));
        }
    }
}
