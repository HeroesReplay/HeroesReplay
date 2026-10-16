using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static HeroesReplay.Tests.Unit.HeroesProfile.FakeHeroesProfileApi;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

/// <summary>A player's record on a hero, from <c>players/heroes</c> (#433).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class PlayerHeroStatsTests : IDisposable
{
    /// <summary>The shape measured on 2026-10-16 (Risllot#2619, EU, Kael'thas), cut down.</summary>
    private const string KaelRow =
        "[{\"battletag\":\"Risllot#2619\",\"region\":2,\"name\":\"Kael'thas\",\"hero\":{\"id\":27,\"name\":\"Kael'thas\",\"attribute_id\":\"Kael\"},"
        + "\"wins\":27,\"losses\":27,\"games_played\":54,\"kda\":6.12,\"win_rate\":50,\"sl_mmr_data\":2300,\"sl_rank_tier\":\"Silver 1\"}]";

    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "hr-player-stats-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose() => TestTemp.Delete(folder);

    [Fact]
    public void Parse_ReadsTheHerosRow()
    {
        PlayerHeroStats stats = PlayerHeroStatsJson.Parse(KaelRow, "Kael", "Kael'thas");

        Assert.Equal(new PlayerHeroStats(27, 27, 54, 6.12, 2300, "Silver 1"), stats);
        Assert.Equal(0.5, stats.WinRate);
    }

    [Fact]
    public void Parse_TakesAnMmrObject_AndNoRowMeansNoGames()
    {
        string withObject = KaelRow.Replace(
            "\"sl_mmr_data\":2300",
            "\"sl_mmr_data\":{\"mmr\":2412,\"win\":27}"
        );

        Assert.Equal(2412, PlayerHeroStatsJson.Parse(withObject, "Kael", null).HeroMmr);
        Assert.Null(PlayerHeroStatsJson.Parse("[]", "Kael", "Kael'thas"));
        Assert.Null(PlayerHeroStatsJson.Parse("{\"async\":true}", "Kael", "Kael'thas"));
        Assert.Null(PlayerHeroStatsJson.Parse("not json", "Kael", "Kael'thas"));
        const string twoRows =
            "[{\"hero\":{\"attribute_id\":\"Kael\"},\"wins\":1,\"losses\":1,\"games_played\":2},"
            + "{\"hero\":{\"attribute_id\":\"Jain\"},\"wins\":3,\"losses\":1,\"games_played\":4}]";
        Assert.Equal(4, PlayerHeroStatsJson.Parse(twoRows, "Jain", null).Games);
        Assert.Null(PlayerHeroStatsJson.Parse(twoRows, "Wiza", "Li-Ming"));
    }

    [Fact]
    public async Task Lookup_AsksOnceWithTheStormLeagueQuery_ThenUsesTheCache()
    {
        var api = new FakeHeroesProfileApi();
        api.Answer(PlayerHeroStatsFetch.PlayersHeroesPath, Accepted("job"));
        api.Job("job", Ok(KaelRow));
        PlayerHeroStatsFetch fetch = Fetch(api);

        PlayerHeroLookup first = await fetch.LookupAsync(
            "Risllot#2619",
            2,
            "Kael'thas",
            "Kael",
            CancellationToken.None
        );
        PlayerHeroLookup second = await fetch.LookupAsync(
            "risllot#2619",
            2,
            "Kael'thas",
            "Kael",
            CancellationToken.None
        );

        Assert.True(first.IsKnown);
        Assert.False(first.Cached);
        Assert.Equal(54, first.Stats.Games);
        Assert.True(second.Cached);
        Assert.Equal(first.Stats, second.Stats);
        var call = Assert.Single(api.Calls);
        Assert.Equal("Risllot#2619", call.Query["battletag"]);
        Assert.Equal("2", call.Query["region"]);
        Assert.Equal("sl", call.Query["game_type"]);
        Assert.Equal("Kael'thas", call.Query["hero"]);
        Assert.Equal(1, fetch.Store.CallsThisWeek(api.Now));
    }

    [Fact]
    public async Task Lookup_AsksAgainOnceTheRecordIsAWeekOld()
    {
        var api = new FakeHeroesProfileApi();
        api.Answer(PlayerHeroStatsFetch.PlayersHeroesPath, Ok(KaelRow));
        PlayerHeroStatsFetch fetch = Fetch(api);

        await fetch.LookupAsync("Risllot#2619", 2, "Kael'thas", "Kael", CancellationToken.None);
        api.Now += TimeSpan.FromDays(7);
        PlayerHeroLookup again = await fetch.LookupAsync(
            "Risllot#2619",
            2,
            "Kael'thas",
            "Kael",
            CancellationToken.None
        );

        Assert.False(again.Cached);
        Assert.Equal(2, api.Calls.Count);
    }

    [Theory]
    [InlineData(403, "player_unavailable", PlayerHeroLookupStatus.Private)]
    [InlineData(404, "player_not_found", PlayerHeroLookupStatus.NotFound)]
    [InlineData(422, "unknown_hero", PlayerHeroLookupStatus.Unavailable)]
    public async Task Lookup_KeepsAPrivateMissingOrRefusedAnswerForADay(
        int status,
        string code,
        PlayerHeroLookupStatus expected
    )
    {
        var api = new FakeHeroesProfileApi();
        api.Answer(PlayerHeroStatsFetch.PlayersHeroesPath, Error(status, code));
        PlayerHeroStatsFetch fetch = Fetch(api);

        PlayerHeroLookup first = await fetch.LookupAsync(
            "Somebody#1",
            1,
            "Valla",
            "Demo",
            CancellationToken.None
        );
        PlayerHeroLookup cached = await fetch.LookupAsync(
            "Somebody#1",
            1,
            "Valla",
            "Demo",
            CancellationToken.None
        );
        api.Now += TimeSpan.FromDays(1);
        await fetch.LookupAsync("Somebody#1", 1, "Valla", "Demo", CancellationToken.None);

        Assert.Equal(expected, first.Status);
        Assert.Equal(expected, cached.Status);
        Assert.True(cached.Cached);
        Assert.Equal(2, api.Calls.Count);
        Assert.False(fetch.Stopped);
    }

    [Fact]
    public async Task Lookup_StopsForGood_WhenTheKeyIsRefused()
    {
        var api = new FakeHeroesProfileApi();
        api.Answer(PlayerHeroStatsFetch.PlayersHeroesPath, Error(403, "endpoint_not_in_plan"));
        PlayerHeroStatsFetch fetch = Fetch(api);

        PlayerHeroLookup refused = await fetch.LookupAsync(
            "Somebody#1",
            1,
            "Valla",
            "Demo",
            CancellationToken.None
        );
        PlayerHeroLookup after = await fetch.LookupAsync(
            "Other#2",
            1,
            "Valla",
            "Demo",
            CancellationToken.None
        );

        Assert.Equal(PlayerHeroLookupStatus.Refused, refused.Status);
        Assert.True(fetch.Stopped);
        Assert.Equal(PlayerHeroLookupStatus.Refused, after.Status);
        Assert.Single(api.Calls);
    }

    [Fact]
    public async Task Lookup_AsksNothingPastTheBudget_OrForABadBattleTag()
    {
        var api = new FakeHeroesProfileApi();
        api.Answer(PlayerHeroStatsFetch.PlayersHeroesPath, Ok(KaelRow));
        PlayerHeroStatsFetch fetch = Fetch(api, new PlayerStatsSettings { WeeklyCallBudget = 1 });

        await fetch.LookupAsync("Risllot#2619", 2, "Kael'thas", "Kael", CancellationToken.None);
        PlayerHeroLookup over = await fetch.LookupAsync(
            "Other#2",
            2,
            "Kael'thas",
            "Kael",
            CancellationToken.None
        );
        PlayerHeroLookup bad = await fetch.LookupAsync(
            "NoNumber",
            2,
            "Kael'thas",
            "Kael",
            CancellationToken.None
        );
        PlayerHeroLookup ptr = await fetch.LookupAsync(
            "Other#2",
            98,
            "Kael'thas",
            "Kael",
            CancellationToken.None
        );

        Assert.Equal(PlayerHeroLookupStatus.OverBudget, over.Status);
        Assert.Equal(PlayerHeroLookupStatus.Unavailable, bad.Status);
        Assert.Equal(PlayerHeroLookupStatus.Unavailable, ptr.Status);
        Assert.Single(api.Calls);
        api.Now += TimeSpan.FromDays(7);
        Assert.Equal(0, fetch.Store.CallsThisWeek(api.Now));
    }

    [Fact]
    public async Task Lookup_DoesNotKeepAnAnswerThatNeverCame()
    {
        var api = new FakeHeroesProfileApi();
        api.Answer(PlayerHeroStatsFetch.PlayersHeroesPath, Error(503));
        PlayerHeroStatsFetch fetch = Fetch(api);

        PlayerHeroLookup failed = await fetch.LookupAsync(
            "Somebody#1",
            1,
            "Valla",
            "Demo",
            CancellationToken.None
        );

        Assert.Equal(PlayerHeroLookupStatus.Unavailable, failed.Status);
        Assert.Null(
            fetch.Store.Fresh(
                "Somebody#1",
                1,
                "Demo",
                api.Now,
                TimeSpan.FromDays(7),
                TimeSpan.FromDays(1)
            )
        );
    }

    [Fact]
    public void Store_PurgesAPlayer_AndReadsABrokenFileAsMissing()
    {
        var store = new PlayerHeroStatsStore(folder);
        var now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        store.Write(
            "Risllot#2619",
            2,
            "Kael",
            new PlayerHeroLookup(
                PlayerHeroLookupStatus.Known,
                new PlayerHeroStats(27, 27, 54, null, null, null)
            ),
            now
        );

        Assert.NotNull(
            store.Fresh("RISLLOT#2619", 2, "Kael", now, TimeSpan.FromDays(7), TimeSpan.FromDays(1))
        );
        Assert.Null(
            store.Fresh("Risllot#2619", 1, "Kael", now, TimeSpan.FromDays(7), TimeSpan.FromDays(1))
        );
        Assert.True(store.Purge("Risllot#2619", 2));
        Assert.False(store.Purge("Risllot#2619", 2));

        File.WriteAllText(store.PathFor("Broken#1", 1), "{ nope");
        Assert.Null(
            store.Fresh("Broken#1", 1, "Kael", now, TimeSpan.FromDays(7), TimeSpan.FromDays(1))
        );
        Assert.Matches(
            "^2-[0-9a-f]{16}\\.json$",
            PlayerHeroStatsStore.FileNameFor("Risllot#2619", 2)
        );
        Assert.Equal(
            Path.Combine("data", "HeroesProfile", "players"),
            PlayerHeroStatsStore.FolderFor("data")
        );
    }

    [Fact]
    public async Task Privacy_TheFirstReadOnlySetsTheCursor_ThenPurgesWhoWentPrivate()
    {
        var api = new FakeHeroesProfileApi();
        api.Answer(
            PlayerPrivacySync.PrivacyPath,
            Ok(
                "{\"changes\":[{\"battletag\":\"Risllot#2619\",\"region\":2,\"state\":\"private\",\"changed_at\":\"2026-10-16T13:00:00+00:00\"},{\"battletag\":\"Open#3\",\"region\":1,\"state\":\"public\"}],\"next_since\":\"2026-10-16T13:00:00+00:00\",\"next_after_id\":1055,\"has_more\":true}"
            ),
            Ok("{\"changes\":[],\"next_since\":null,\"next_after_id\":null,\"has_more\":false}")
        );
        var store = new PlayerHeroStatsStore(folder);
        var lookup = new PlayerHeroLookup(
            PlayerHeroLookupStatus.Known,
            new PlayerHeroStats(1, 1, 2, null, null, null)
        );
        store.Write("Risllot#2619", 2, "Kael", lookup, api.Now);
        store.Write("Open#3", 1, "Kael", lookup, api.Now);
        PlayerPrivacySync sync = Sync(api, store);

        Assert.True(sync.IsDue());
        Assert.Equal(0, await sync.SyncAsync(CancellationToken.None));
        Assert.Empty(api.Calls);
        Assert.False(sync.IsDue());

        api.Now += TimeSpan.FromHours(12);
        Assert.True(sync.IsDue());
        Assert.Equal(1, await sync.SyncAsync(CancellationToken.None));

        Assert.Equal(2, api.Calls.Count);
        Assert.Equal("1000", api.Calls[0].Query["limit"]);
        Assert.Equal("1055", api.Calls[1].Query["after_id"]);
        Assert.True(store.IsPrivate("Risllot#2619", 2));
        Assert.Equal(
            PlayerHeroLookupStatus.Private,
            store
                .Fresh(
                    "Risllot#2619",
                    2,
                    "Kael",
                    api.Now,
                    TimeSpan.FromDays(7),
                    TimeSpan.FromDays(1)
                )
                .Status
        );
        Assert.False(store.IsPrivate("Open#3", 1));
        Assert.NotNull(
            store.Fresh("Open#3", 1, "Kael", api.Now, TimeSpan.FromDays(7), TimeSpan.FromDays(1))
        );
        Assert.Equal(1055, store.ReadPrivacy().AfterId);
    }

    private PlayerHeroStatsFetch Fetch(
        FakeHeroesProfileApi api,
        PlayerStatsSettings settings = null
    ) =>
        new(
            new HeroesProfileCaller(api, api.Pacer(), NullLogger.Instance, api.Clock, api.Delay),
            new PlayerHeroStatsStore(folder),
            settings ?? new PlayerStatsSettings(),
            NullLogger.Instance,
            api.Clock
        );

    private static PlayerPrivacySync Sync(FakeHeroesProfileApi api, PlayerHeroStatsStore store) =>
        new(
            new HeroesProfileCaller(api, api.Pacer(), NullLogger.Instance, api.Clock, api.Delay),
            store,
            new PlayerStatsSettings(),
            NullLogger.Instance,
            api.Clock
        );
}
