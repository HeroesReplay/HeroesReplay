using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Shared;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

/// <summary>
/// #429: a replay's tier for a rank reward, from its players' average MMR on its own game type's
/// ladder. Each type has its own: on 2026-10-13 2,750 was Platinum 4 in Storm League and Diamond 4
/// in Quick Match.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayTierLookupTests
{
    [Fact]
    public async Task QuickMatch_AsksTheQuickMatchLadder_ForTheAverageMmr_Once()
    {
        var http = new ScriptedHttp(request =>
            request.RequestUri.AbsolutePath.EndsWith("/mmr/tier", StringComparison.Ordinal)
                ? Json("""{"game_type":"qm","mmr":2750,"tier":"Diamond 4"}""")
                : Json(
                    """{"game_type":"Quick Match","players":[{"player_mmr":2700},{"player_mmr":2800},{"player_mmr":null}]}"""
                )
        );
        using ServiceProvider services = HeroesProfileHttpServices(http);
        IReplayTierLookup lookup = Service(services);

        string tier = await lookup.GetTierAsync(
            65956380,
            GameType.QuickMatch,
            CancellationToken.None
        );
        string again = await lookup.GetTierAsync(
            65956380,
            GameType.QuickMatch,
            CancellationToken.None
        );

        Assert.Equal("Diamond 4", tier);
        Assert.Equal("Diamond 4", again);
        Assert.Equal(
            new[] { "/api/external/v1/replay/65956380", "/api/external/v1/mmr/tier" },
            http.Paths
        );
        string tierQuery = Assert.Single(http.Queries.Where(query => query.Contains("mmr=")));
        Assert.Contains("game_type=Quick%20Match", tierQuery, StringComparison.Ordinal);
        Assert.Contains("mmr=2750", tierQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoMmr_IsNoTier_AndAsksNoLadder()
    {
        var http = new ScriptedHttp(_ => Json("""{"game_type":"ARAM","players":[]}"""));
        using ServiceProvider services = HeroesProfileHttpServices(http);

        string tier = await Service(services)
            .GetTierAsync(1, GameType.ARAM, CancellationToken.None);

        Assert.Null(tier);
        Assert.DoesNotContain(
            http.Paths,
            path => path.EndsWith("/mmr/tier", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task AFailedLookup_IsNoTier_NotAnError()
    {
        var http = new ScriptedHttp(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(
                """{"error":{"code":"replay_not_found"}}""",
                Encoding.UTF8,
                "application/json"
            ),
        });
        using ServiceProvider services = HeroesProfileHttpServices(http);

        string tier = await Service(services)
            .GetTierAsync(2, GameType.StormLeague, CancellationToken.None);

        Assert.Null(tier);
    }

    private static HeroesProfileService Service(ServiceProvider services) =>
        new(
            NullLogger<HeroesProfileService>.Instance,
            new MemoryCache(new MemoryCacheOptions()),
            new CancellationTokenProvider(CancellationToken.None),
            new AppSettings { HeroesProfileApi = new HeroesProfileApiSettings() },
            HeroesProfileClientFactory.Create(
                "test-key",
                services
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(HeroesProfileHttp.ClientName),
                new Uri("https://www.heroesprofile.com/api/external/v1/")
            )
        );

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static ServiceProvider HeroesProfileHttpServices(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddHttpClient(
                HeroesProfileHttp.ClientName,
                client => client.Timeout = Timeout.InfiniteTimeSpan
            )
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler(HeroesProfileHttp.ClientName, HeroesProfileHttp.Configure);
        return services.BuildServiceProvider();
    }

    private sealed class ScriptedHttp : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;

        public ScriptedHttp(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            this.respond = respond;
        }

        public List<string> Paths { get; } = new();

        public List<string> Queries { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Paths.Add(request.RequestUri.AbsolutePath);
            Queries.Add(request.RequestUri.Query);
            return Task.FromResult(respond(request));
        }
    }
}
