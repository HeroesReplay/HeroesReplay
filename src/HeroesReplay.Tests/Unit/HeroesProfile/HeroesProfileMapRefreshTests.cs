using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

/// <summary>
/// #425: Heroes Profile's <c>/maps</c> list, saved in <c>Data\HeroesProfile\maps.json</c> and
/// fetched at most once per <c>MapsRefreshInterval</c>.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesProfileMapRefreshTests : IDisposable
{
    // Heroes Profile's /maps answer on 2026-10-13, trimmed.
    private const string Answer = """
        {"maps":[
          {"map_id":15,"name":"Alterac Pass","short_name":"AlteracPass","type":"standard","ranked_rotation":1,"playable":1,"sanitized_map_name":"alterac_pass"},
          {"map_id":2,"name":"Blackheart's Bay","short_name":"BlackheartsBay","type":"standard","ranked_rotation":0,"playable":1,"sanitized_map_name":"blackhearts_bay"},
          {"map_id":21,"name":"Braxis Outpost","short_name":"BraxisOutpost","type":"ARAM","ranked_rotation":0,"playable":1,"sanitized_map_name":"braxis_outpost"},
          {"map_id":7,"name":"Hanamura Temple","short_name":"Hanamura","type":"standard","ranked_rotation":1,"playable":1,"sanitized_map_name":"hanamura_temple"}
        ]}
        """;

    private static readonly DateTimeOffset Now = new(2026, 10, 13, 16, 0, 0, TimeSpan.Zero);

    private readonly string data = Path.Combine(
        Path.GetTempPath(),
        "hr-maps-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(data))
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void FromJson_ReadsRankedRotation_OneIsRankedAndAnythingElseIsNot()
    {
        HeroesProfileMapList list = HeroesProfileMapList.FromJson(
            """
            {"maps":[
              {"name":"Cursed Hollow","type":"standard","ranked_rotation":1,"playable":1},
              {"name":"Blackheart's Bay","type":"standard","ranked_rotation":0,"playable":1},
              {"name":"Warhead Junction","type":"standard","ranked_rotation":"-","playable":1},
              {"name":"Sky Temple","type":"standard","ranked_rotation":"1","playable":"1"},
              {"name":"Lost Cavern","type":"ARAM","playable":1},
              {"name":"","type":"standard","ranked_rotation":1}
            ]}
            """,
            Now
        );

        Assert.Equal(Now, list.FetchedAtUtc);
        Assert.Equal(
            new[]
            {
                "Cursed Hollow",
                "Blackheart's Bay",
                "Warhead Junction",
                "Sky Temple",
                "Lost Cavern",
            },
            list.Maps.Select(map => map.Name)
        );
        Assert.Equal(new[] { "Cursed Hollow", "Sky Temple" }, list.RankedNames);
        Assert.All(list.Maps, map => Assert.True(map.Playable));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"maps":[]}""")]
    [InlineData("""{"error":{"code":"rate_limited"}}""")]
    public void FromJson_NoMaps_IsNull(string json)
    {
        Assert.Null(HeroesProfileMapList.FromJson(json, Now));
    }

    [Fact]
    public void Store_RoundTripsTheListAndItsFetchTime()
    {
        var store = new HeroesProfileMapStore(data);
        HeroesProfileMapList list = HeroesProfileMapList.FromJson(Answer, Now);

        store.Write(list);
        HeroesProfileMapList read = store.Read();

        Assert.Equal(Path.Combine(data, "HeroesProfile", "maps.json"), store.FilePath);
        Assert.Equal(Now, read.FetchedAtUtc);
        Assert.Equal(list.Maps, read.Maps);
    }

    [Fact]
    public void Store_WithoutADataDirectory_ReadsNothingAndWritesNothing()
    {
        var store = new HeroesProfileMapStore(null);

        store.Write(HeroesProfileMapList.FromJson(Answer, Now));

        Assert.Null(store.FilePath);
        Assert.Null(store.Read());
    }

    [Fact]
    public async Task NoSavedList_FetchesAndSavesIt()
    {
        var api = new FakeApi(200, Answer);

        HeroesProfileMapList list = await Refresh(api).RefreshIfDueAsync(CancellationToken.None);

        Assert.Equal(1, api.Calls);
        Assert.Equal("maps", api.Path);
        Assert.Equal(new[] { "Alterac Pass", "Hanamura Temple" }, list.RankedNames);
        Assert.Equal(Now, new HeroesProfileMapStore(data).Read().FetchedAtUtc);
    }

    [Fact]
    public async Task SavedListWithinTheInterval_IsReusedWithoutACall()
    {
        new HeroesProfileMapStore(data).Write(
            HeroesProfileMapList.FromJson(Answer, Now - TimeSpan.FromHours(23))
        );
        var api = new FakeApi(200, Answer);

        HeroesProfileMapList list = await Refresh(api).RefreshIfDueAsync(CancellationToken.None);

        Assert.Equal(0, api.Calls);
        Assert.Equal(Now - TimeSpan.FromHours(23), list.FetchedAtUtc);
    }

    [Fact]
    public async Task SavedListPastTheInterval_IsFetchedAgain()
    {
        new HeroesProfileMapStore(data).Write(
            HeroesProfileMapList.FromJson(Answer, Now - TimeSpan.FromHours(25))
        );
        var api = new FakeApi(200, Answer);

        HeroesProfileMapList list = await Refresh(api).RefreshIfDueAsync(CancellationToken.None);

        Assert.Equal(1, api.Calls);
        Assert.Equal(Now, list.FetchedAtUtc);
    }

    [Theory]
    [InlineData(429, """{"error":{"code":"quota_exceeded"}}""")]
    [InlineData(403, """{"error":{"code":"endpoint_not_in_plan"}}""")]
    [InlineData(500, "")]
    [InlineData(200, "not json")]
    public async Task FailedFetch_KeepsTheSavedList(int status, string body)
    {
        DateTimeOffset saved = Now - TimeSpan.FromDays(3);
        new HeroesProfileMapStore(data).Write(HeroesProfileMapList.FromJson(Answer, saved));
        var api = new FakeApi(status, body);

        HeroesProfileMapList list = await Refresh(api).RefreshIfDueAsync(CancellationToken.None);

        Assert.Equal(1, api.Calls);
        Assert.Equal(saved, list.FetchedAtUtc);
        Assert.Equal(saved, new HeroesProfileMapStore(data).Read().FetchedAtUtc);
    }

    [Fact]
    public async Task UnreachableApi_WithNoSavedList_IsNull()
    {
        var api = new FakeApi(0, null) { Throw = new HttpRequestExceptionStub() };

        Assert.Null(await Refresh(api).RefreshIfDueAsync(CancellationToken.None));
        Assert.Equal(1, api.Calls);
    }

    [Fact]
    public async Task ZeroInterval_NeverFetches_ButReadsTheSavedList()
    {
        new HeroesProfileMapStore(data).Write(
            HeroesProfileMapList.FromJson(Answer, Now - TimeSpan.FromDays(30))
        );
        var api = new FakeApi(200, Answer);

        HeroesProfileMapList list = await Refresh(api, TimeSpan.Zero)
            .RefreshIfDueAsync(CancellationToken.None);

        Assert.Equal(0, api.Calls);
        Assert.NotNull(list);
    }

    private HeroesProfileMapRefresh Refresh(FakeApi api, TimeSpan? interval = null) =>
        new(
            api,
            new HeroesProfileMapStore(data),
            interval ?? TimeSpan.FromDays(1),
            NullLogger<HeroesProfileMapRefresh>.Instance,
            () => Now
        );

    private sealed class HttpRequestExceptionStub : Exception
    {
        public HttpRequestExceptionStub()
            : base("No such host is known.") { }
    }

    private sealed class FakeApi : IHeroStatsApi
    {
        private readonly int status;
        private readonly string body;

        public FakeApi(int status, string body)
        {
            this.status = status;
            this.body = body;
        }

        public int Calls { get; private set; }

        public string Path { get; private set; }

        public Exception Throw { get; init; }

        public Task<HeroesProfileGlobalAnswer> GetAsync(
            string path,
            IReadOnlyList<KeyValuePair<string, string>> query,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            Path = path;
            if (Throw != null)
            {
                throw Throw;
            }

            return Task.FromResult(HeroesProfileGlobalAnswer.From(status, body, null));
        }

        public Task<HeroesProfileGlobalAnswer> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }
}
