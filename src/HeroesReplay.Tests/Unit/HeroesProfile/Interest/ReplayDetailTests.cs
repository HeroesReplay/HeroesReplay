using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.HeroesProfile.Interest;
using HeroesReplay.Core.Shared;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile.Interest;

/// <summary>
/// A listed replay's score screen from <c>/replay/{id}</c> (#510), in the shape Heroes Profile
/// answered for 66231266 on 2026-10-25: one array of player objects per team.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayDetailTests
{
    private const string Detail = """
        {"game_type":"Storm League","game_length":1366,"players":[
          [{"team":0,"player_mmr":2400,"hero":{"name":"Imperius"},"score":{"kills":12,"deaths":5,"assists":11,"takedowns":23,"highest_kill_streak":12,"multikill":5,"time_on_fire":76,"hero_damage":69826}},
           {"team":0,"player_mmr":2500,"hero":{"name":"Azmodan"},"score":{"kills":5,"deaths":2,"assists":14,"takedowns":19,"highest_kill_streak":9,"multikill":"4","time_on_fire":416}}],
          [{"team":1,"player_mmr":2600,"hero":{"name":"Valla"},"score":{"kills":8,"deaths":9,"assists":6,"takedowns":14,"highest_kill_streak":6,"multikill":2,"time_on_fire":75}}]
        ]}
        """;

    [Fact]
    public async Task Detail_ReadsEachPlayersScoreLine_AndTheLength()
    {
        var http = new ScriptedHttp(_ => Json(Detail));
        using ServiceProvider services = HeroesProfileHttpServices(http);

        ReplayDetail detail = await Service(services)
            .GetDetailAsync(66231266, CancellationToken.None);

        Assert.Equal(66231266, detail.ReplayId);
        Assert.Equal(1366, detail.LengthSeconds);
        Assert.Equal(
            new[]
            {
                new ReplayScoreLine(0, "Imperius", 12, 5, 11, 23, 12, 5, 76),
                new ReplayScoreLine(0, "Azmodan", 5, 2, 14, 19, 9, 4, 416),
                new ReplayScoreLine(1, "Valla", 8, 9, 6, 14, 6, 2, 75),
            },
            detail.Players
        );
    }

    [Fact]
    public async Task ScoringACandidate_AndThenItsRankBadge_IsOneCall()
    {
        var http = new ScriptedHttp(request =>
            request.RequestUri.AbsolutePath.EndsWith("/mmr/tier", StringComparison.Ordinal)
                ? Json("""{"game_type":"sl","mmr":2500,"tier":"Gold 3"}""")
                : Json(Detail)
        );
        using ServiceProvider services = HeroesProfileHttpServices(http);
        HeroesProfileService service = Service(services);
        var listed = new HeroesProfileReplay { Id = 66231266, GameType = "Storm League" };

        await service.GetDetailAsync(66231266, CancellationToken.None);
        await service.EnrichRankAsync(listed, CancellationToken.None);

        Assert.Equal("Gold 3", listed.Rank);
        Assert.Single(
            http.Paths,
            path => path.EndsWith("/replay/66231266", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task NoPlayers_IsNoDetail()
    {
        var http = new ScriptedHttp(_ => Json("""{"game_type":"Storm League","players":[]}"""));
        using ServiceProvider services = HeroesProfileHttpServices(http);

        Assert.Null(await Service(services).GetDetailAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task AMissingReplay_IsNoDetail_NotAnError()
    {
        var http = new ScriptedHttp(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using ServiceProvider services = HeroesProfileHttpServices(http);

        Assert.Null(await Service(services).GetDetailAsync(1, CancellationToken.None));
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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Paths.Add(request.RequestUri.AbsolutePath);
            return Task.FromResult(respond(request));
        }
    }
}
