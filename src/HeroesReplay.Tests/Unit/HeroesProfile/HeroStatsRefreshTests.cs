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

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class HeroStatsRefreshTests : IDisposable
{
    private const string Patches = """
        {"patches":[
          {"game_version":"2.55.17.98025","valid_globals":true},
          {"game_version":"2.57.0.98348","valid_globals":true}
        ]}
        """;

    private const string Heroes = """
        {"heroes":[
          {"id":64,"name":"Valla","attribute_id":"Demo"},
          {"id":92,"name":"Xal'atath","attribute_id":"HXAL"},
          {"id":7,"name":"Unplayed","attribute_id":"UNPL"}
        ]}
        """;

    private const string MapStats = """
        {
          "Garden of Terror": {"data":[
            {"hero_id":64,"wins":299,"losses":254,"games_played":553,"ban_rate":9.76},
            {"hero_id":92,"wins":138,"losses":72,"games_played":210,"ban_rate":86.11}
          ]},
          "Cursed Hollow": {"data":[
            {"hero_id":64,"wins":231,"losses":273,"games_played":504,"ban_rate":8.18}
          ]}
        }
        """;

    private const string VallaMatchups = """
        {"ally":[],"enemy":[{"hero":{"attribute_id":"HXAL"},"wins":117,"losses":220,"games_played":337,"win_rate":65.28}],"combined":[]}
        """;

    private const string XalMatchups = """
        {"ally":[],"enemy":[{"hero":{"attribute_id":"Demo"},"wins":220,"losses":117,"games_played":337,"win_rate":34.72}],"combined":[]}
        """;

    private static readonly DateTimeOffset Start = new DateTimeOffset(
        2026,
        10,
        8,
        12,
        0,
        0,
        TimeSpan.Zero
    );

    private readonly string data = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-refresh-" + Guid.NewGuid().ToString("N")
    );

    private DateTimeOffset now = Start;
    private readonly List<TimeSpan> delays = new List<TimeSpan>();

    public void Dispose()
    {
        if (Directory.Exists(data))
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshDue_CollectsJobsAndWritesTheNewestPatch()
    {
        var api = new FakeApi();
        api.Answer("patches", Ok(Patches));
        api.Answer("heroes", Ok(Heroes));
        api.Answer("heroes/stats", Accepted("job-maps", TimeSpan.FromSeconds(10)));
        api.Job("job-maps", Accepted(null, TimeSpan.FromSeconds(7)), Ok(MapStats));
        api.Answer("heroes/matchups?Valla", Accepted("job-valla", null));
        api.Job("job-valla", Ok(VallaMatchups));
        api.Answer("heroes/matchups?Xal'atath", Ok(XalMatchups));
        HeroStatsRefresh refresh = Refresh(api);

        IReadOnlyList<string> written = await refresh.RefreshDueAsync(
            false,
            CancellationToken.None
        );

        string path = Assert.Single(written);
        Assert.EndsWith(Path.Combine("hero-stats", "2.57-sl.json"), path, StringComparison.Ordinal);
        HeroStatsSnapshot snapshot = refresh.Store.Read("2.57", "sl");
        Assert.Equal("2.57", snapshot.Patch);
        Assert.Equal(now, snapshot.FetchedAtUtc);
        Assert.Equal(new[] { "Demo", "HXAL" }, snapshot.Heroes.Select(hero => hero.AttributeId));
        HeroStats valla = snapshot.Find("Demo");
        Assert.Equal(530, valla.Wins);
        Assert.Equal(1057, valla.Games);
        Assert.Equal(34.7, valla.Enemy("HXAL").WinRate.Value, 1);
        Assert.Equal(86.11, snapshot.Find("HXAL").Map("Garden of Terror").BanRate);

        // The stats call asks for one major patch, grouped by map; each hero's matchups name the hero.
        FakeApi.Call stats = Assert.Single(api.Calls, call => call.Path == "heroes/stats");
        Assert.Equal("major", stats.Query["timeframe_type"]);
        Assert.Equal("2.57", stats.Query["timeframe"]);
        Assert.Equal("sl", stats.Query["game_type"]);
        Assert.Equal("true", stats.Query["group_by_map"]);
        Assert.DoesNotContain(
            api.Calls,
            call => call.Path == "heroes/matchups" && call.Query["hero"] == "Unplayed"
        );

        // Polls wait for Retry-After (10 s, then 7 s), and the default 10 s without one.
        Assert.Contains(TimeSpan.FromSeconds(10), delays);
        Assert.Contains(TimeSpan.FromSeconds(7), delays);
        Assert.Equal(3, api.JobPolls);
    }

    [Fact]
    public async Task RefreshDue_SpacesRequestsUnderThePerMinuteLimit()
    {
        FakeApi api = Complete();
        HeroStatsRefresh refresh = Refresh(api);

        await refresh.RefreshDueAsync(false, CancellationToken.None);

        for (int i = 1; i < api.Calls.Count; i++)
        {
            Assert.True(
                api.Calls[i].At - api.Calls[i - 1].At >= TimeSpan.FromMilliseconds(1100),
                $"Call {i} came {api.Calls[i].At - api.Calls[i - 1].At} after the one before."
            );
        }
    }

    [Fact]
    public async Task RefreshDue_SkipsAFreshFileAndForceFetchesIt()
    {
        FakeApi api = Complete();
        HeroStatsRefresh refresh = Refresh(api);
        await refresh.RefreshDueAsync(false, CancellationToken.None);
        int calls = api.Calls.Count;

        now = now.AddHours(23);
        Assert.Empty(await refresh.RefreshDueAsync(false, CancellationToken.None));
        Assert.Equal(calls + 1, api.Calls.Count); // only /patches

        Assert.Single(await refresh.RefreshDueAsync(true, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshDue_WaitsForRetryAfterOnARateLimit()
    {
        FakeApi api = Complete();
        api.Answer(
            "heroes/stats",
            HeroesProfileGlobalAnswer.From(
                429,
                "{\"error\":{\"code\":\"rate_limited\"}}",
                TimeSpan.FromSeconds(42)
            ),
            Ok(MapStats)
        );
        HeroStatsRefresh refresh = Refresh(api);

        Assert.Single(await refresh.RefreshDueAsync(false, CancellationToken.None));
        Assert.Contains(TimeSpan.FromSeconds(42), delays);
        Assert.Equal(2, api.Calls.Count(call => call.Path == "heroes/stats"));
    }

    [Theory]
    [InlineData(401, "unauthenticated")]
    [InlineData(403, "endpoint_not_in_plan")]
    public async Task RefreshDue_StopsForGoodWhenTheKeyIsRefused(int status, string code)
    {
        FakeApi api = Complete();
        api.Answer(
            "heroes/stats",
            HeroesProfileGlobalAnswer.From(status, "{\"error\":{\"code\":\"" + code + "\"}}", null)
        );
        HeroStatsRefresh refresh = Refresh(api);

        Assert.Empty(await refresh.RefreshDueAsync(false, CancellationToken.None));
        Assert.True(refresh.Stopped);
        int calls = api.Calls.Count;

        Assert.Empty(await refresh.RefreshDueAsync(true, CancellationToken.None));
        Assert.Equal(calls, api.Calls.Count);
        Assert.Null(refresh.Store.Read("2.57", "sl"));
    }

    [Fact]
    public async Task RefreshDue_SkipsARejectedPatchUntilTheNextRefreshIsDue()
    {
        FakeApi api = Complete();
        api.Answer(
            "heroes/stats",
            HeroesProfileGlobalAnswer.From(
                422,
                "{\"error\":{\"code\":\"timeframe_unavailable\"}}",
                null
            ),
            Ok(MapStats)
        );
        HeroStatsRefresh refresh = Refresh(api);

        Assert.Empty(await refresh.RefreshDueAsync(false, CancellationToken.None));
        Assert.False(refresh.Stopped);

        now = now.AddHours(1);
        Assert.Empty(await refresh.RefreshDueAsync(false, CancellationToken.None));
        Assert.Single(api.Calls, call => call.Path == "heroes/stats");

        now = now.AddHours(24);
        Assert.Single(await refresh.RefreshDueAsync(false, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshDue_KeepsAHeroWhoseMatchupsFailed()
    {
        FakeApi api = Complete();
        api.Answer(
            "heroes/matchups?Xal'atath",
            HeroesProfileGlobalAnswer.From(404, "{\"error\":{\"code\":\"not_found\"}}", null)
        );
        HeroStatsRefresh refresh = Refresh(api);

        Assert.Single(await refresh.RefreshDueAsync(false, CancellationToken.None));
        HeroStats xal = refresh.Store.Read("2.57", "sl").Find("HXAL");
        Assert.Empty(xal.Enemies);
        Assert.Equal(210, xal.Games);
    }

    [Fact]
    public async Task RefreshDue_GivesUpOnAJobPastTheTimeout()
    {
        FakeApi api = Complete();
        api.Answer("heroes/stats", Accepted("slow", TimeSpan.FromMinutes(4)));
        api.Job("slow", Accepted(null, TimeSpan.FromMinutes(4)));
        HeroStatsRefresh refresh = Refresh(
            api,
            new HeroStatsSettings { JobTimeout = TimeSpan.FromMinutes(10) }
        );

        await Assert.ThrowsAsync<HeroStatsException>(() =>
            refresh.RefreshDueAsync(false, CancellationToken.None)
        );
        Assert.Null(refresh.Store.Read("2.57", "sl"));
        Assert.False(refresh.Stopped);
    }

    [Fact]
    public async Task RefreshDue_DeletesFilesOfPatchesOlderThanTheNewestTwo()
    {
        FakeApi api = Complete();
        HeroStatsRefresh refresh = Refresh(api);
        refresh.Store.Write(
            new HeroStatsSnapshot
            {
                Patch = "2.53",
                GameType = "sl",
                FetchedAtUtc = Start,
            }
        );
        refresh.Store.Write(
            new HeroStatsSnapshot
            {
                Patch = "2.55",
                GameType = "sl",
                FetchedAtUtc = Start,
            }
        );

        await refresh.RefreshDueAsync(false, CancellationToken.None);

        Assert.Null(refresh.Store.Read("2.53", "sl"));
        Assert.NotNull(refresh.Store.Read("2.55", "sl"));
        Assert.NotNull(refresh.Store.Read("2.57", "sl"));
    }

    [Fact]
    public async Task Run_StopsAfterARefusedKey()
    {
        FakeApi api = Complete();
        api.Answer("patches", HeroesProfileGlobalAnswer.From(401, "{}", null));
        HeroStatsRefresh refresh = Refresh(api);

        await refresh.RunAsync(CancellationToken.None);

        Assert.True(refresh.Stopped);
        Assert.Single(api.Calls);
    }

    private FakeApi Complete()
    {
        var api = new FakeApi();
        api.Answer("patches", Ok(Patches));
        api.Answer("heroes", Ok(Heroes));
        api.Answer("heroes/stats", Ok(MapStats));
        api.Answer("heroes/matchups?Valla", Ok(VallaMatchups));
        api.Answer("heroes/matchups?Xal'atath", Ok(XalMatchups));
        return api;
    }

    private HeroStatsRefresh Refresh(FakeApi api, HeroStatsSettings settings = null)
    {
        api.Clock = () => now;
        return new HeroStatsRefresh(
            api,
            new HeroStatsStore(data),
            settings ?? new HeroStatsSettings(),
            NullLogger<HeroStatsRefresh>.Instance,
            () => now,
            (wait, token) =>
            {
                delays.Add(wait);
                now += wait;
                return Task.CompletedTask;
            }
        );
    }

    private static HeroesProfileGlobalAnswer Ok(string body) =>
        HeroesProfileGlobalAnswer.From(200, body, null);

    private static HeroesProfileGlobalAnswer Accepted(string jobId, TimeSpan? retryAfter) =>
        HeroesProfileGlobalAnswer.From(
            202,
            jobId == null
                ? "{\"async\":true,\"status\":\"pending\"}"
                : "{\"async\":true,\"status\":\"pending\",\"job_id\":\"" + jobId + "\"}",
            retryAfter
        );

    private sealed class FakeApi : IHeroStatsApi
    {
        private readonly Dictionary<string, Queue<HeroesProfileGlobalAnswer>> answers = new();
        private readonly Dictionary<string, Queue<HeroesProfileGlobalAnswer>> jobs = new();

        public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;
        public List<Call> Calls { get; } = new List<Call>();
        public int JobPolls { get; private set; }

        /// <summary>Answers in order; the last one repeats. A key is the path, plus <c>?hero</c> for matchups.</summary>
        public void Answer(string key, params HeroesProfileGlobalAnswer[] replies) =>
            answers[key] = new Queue<HeroesProfileGlobalAnswer>(replies);

        public void Job(string jobId, params HeroesProfileGlobalAnswer[] replies) =>
            jobs[jobId] = new Queue<HeroesProfileGlobalAnswer>(replies);

        public Task<HeroesProfileGlobalAnswer> GetAsync(
            string path,
            IReadOnlyList<KeyValuePair<string, string>> query,
            CancellationToken cancellationToken
        )
        {
            var values = (query ?? Array.Empty<KeyValuePair<string, string>>()).ToDictionary(
                pair => pair.Key,
                pair => pair.Value
            );
            Calls.Add(new Call(path, values, Clock()));
            string key = values.TryGetValue("hero", out string hero) ? path + "?" + hero : path;
            return Task.FromResult(Next(answers, key));
        }

        public Task<HeroesProfileGlobalAnswer> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken
        )
        {
            JobPolls++;
            Calls.Add(new Call("jobs/" + jobId, new Dictionary<string, string>(), Clock()));
            return Task.FromResult(Next(jobs, jobId));
        }

        private static HeroesProfileGlobalAnswer Next(
            Dictionary<string, Queue<HeroesProfileGlobalAnswer>> source,
            string key
        )
        {
            if (
                !source.TryGetValue(key, out Queue<HeroesProfileGlobalAnswer> queue)
                || queue.Count == 0
            )
            {
                return HeroesProfileGlobalAnswer.From(
                    404,
                    "{\"error\":{\"code\":\"not_found\"}}",
                    null
                );
            }

            return queue.Count == 1 ? queue.Peek() : queue.Dequeue();
        }

        public sealed record Call(string Path, Dictionary<string, string> Query, DateTimeOffset At);
    }
}
