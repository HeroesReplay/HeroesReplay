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
/// #425: a reward's search keeps the game type the reward asks for. The download role's list,
/// <c>HeroesProfileApi:GameTypes</c>, is Storm League only, and it used to drop every Quick
/// Match, ARAM and Unranked Draft answer, so those rewards never found a game.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class FilteredReplaySearchTests
{
    private const string Current = "2.57.0.98348";

    [Fact]
    public async Task QuickMatchSearch_KeepsQuickMatchGames_ThoughTheDownloadListIsStormLeague()
    {
        var http = new ScriptedHttp(Page);
        using ServiceProvider services = HeroesProfileHttpServices(http);
        HeroesProfileService service = Service(services);

        List<HeroesProfileReplay> found = (
            await service.GetReplaysByFilters(GameType.QuickMatch, null, "Hanamura Temple")
        ).ToList();

        Assert.Equal(new[] { 302 }, found.Select(replay => replay.Id));
        Assert.Contains(
            http.Queries,
            query =>
                query.Contains("game_type=Quick%20Match", StringComparison.Ordinal)
                && query.Contains("game_map=Hanamura%20Temple", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task SearchWithoutAGameType_KeepsTheDownloadList()
    {
        var http = new ScriptedHttp(Page);
        using ServiceProvider services = HeroesProfileHttpServices(http);
        HeroesProfileService service = Service(services);

        List<HeroesProfileReplay> found = (
            await service.GetReplaysByFilters(null, null, "Hanamura Temple")
        ).ToList();

        Assert.Equal(new[] { 301 }, found.Select(replay => replay.Id));
    }

    [Theory]
    [InlineData("Storm League", "sl", true)]
    [InlineData("Quick Match", "Quick Match", true)]
    [InlineData("Quick Match", "Storm League", false)]
    [InlineData(null, "Storm League", false)]
    [InlineData("ARAM", "", false)]
    public void SameGameType_MatchesNamesAndTheSlShorthand(
        string expected,
        string actual,
        bool same
    )
    {
        Assert.Equal(same, HeroesProfileApiSettings.SameGameType(expected, actual));
    }

    private static HeroesProfileService Service(ServiceProvider services) =>
        new(
            NullLogger<HeroesProfileService>.Instance,
            new MemoryCache(new MemoryCacheOptions()),
            new CancellationTokenProvider(CancellationToken.None),
            new AppSettings
            {
                HeroesProfileApi = new HeroesProfileApiSettings
                {
                    GameTypes = new[] { "Storm League" },
                    ApiMaxReturnedReplays = 1000,
                    FallbackMaxReplayId = 400,
                },
            },
            HeroesProfileClientFactory.Create(
                "test-key",
                services
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(HeroesProfileHttp.ClientName),
                new Uri("https://www.heroesprofile.com/api/external/v1/")
            )
        );

    private static HttpResponseMessage Page(HttpRequestMessage request)
    {
        string played = DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss");
        string rows = string.Join(
            ",",
            new[] { (301, "Storm League"), (302, "Quick Match"), (303, "ARAM") }.Select(row =>
                $$"""{"replayID":{{row.Item1}},"game_type":"{{row.Item2}}","game_version":"{{Current}}","game_map":"Hanamura Temple","game_date":"{{played}}","fingerprint":"abc","downloadable":true,"deleted":0,"region":1,"parsed":1}"""
            )
        );
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"max_replay_id":303,"next_after":303,"replays":[{{rows}}]}""",
                Encoding.UTF8,
                "application/json"
            ),
        };
    }

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

        public List<string> Queries { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Queries.Add(request.RequestUri.Query);
            return Task.FromResult(respond(request));
        }
    }
}
