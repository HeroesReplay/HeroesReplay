using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static HeroesReplay.Tests.Unit.HeroesProfile.FakeHeroesProfileApi;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

/// <summary>The paced Heroes Profile GET the statistics calls and player lookups share (#433).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesProfileCallerTests
{
    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task A202_IsCollectedFromItsJob_AfterRetryAfter()
    {
        var api = new FakeHeroesProfileApi();
        api.Answer("players/heroes", Accepted("job-1", TimeSpan.FromSeconds(10)));
        api.Job("job-1", Accepted("job-1", TimeSpan.FromSeconds(4)), Ok("[1]"));

        HeroesProfileGlobalAnswer answer = await Caller(api)
            .GetAsync("players/heroes", null, JobTimeout, CancellationToken.None);

        Assert.Equal(200, answer.StatusCode);
        Assert.Equal("[1]", answer.Body);
        Assert.Equal(2, api.JobPolls);
        Assert.Contains(TimeSpan.FromSeconds(10), api.Delays);
        Assert.Contains(TimeSpan.FromSeconds(4), api.Delays);
    }

    [Fact]
    public async Task A429_WaitsForRetryAfter_ThenGivesUpAfterFive()
    {
        var api = new FakeHeroesProfileApi();
        api.Answer("a", Error(429, "rate_limited", TimeSpan.FromSeconds(3)), Ok("done"));
        api.Answer("b", Error(429, "rate_limited", TimeSpan.FromSeconds(2)));
        HeroesProfileCaller caller = Caller(api);

        HeroesProfileGlobalAnswer recovered = await caller.GetAsync(
            "a",
            null,
            JobTimeout,
            CancellationToken.None
        );
        HeroStatsException exhausted = await Assert.ThrowsAsync<HeroStatsException>(() =>
            caller.GetAsync("b", null, JobTimeout, CancellationToken.None)
        );

        Assert.Equal("done", recovered.Body);
        Assert.Contains(TimeSpan.FromSeconds(3), api.Delays);
        Assert.Equal(
            HeroesProfileCaller.MaxRateLimited + 1,
            api.Calls.Count(call => call.Path == "b")
        );
        Assert.Equal(429, exhausted.StatusCode);
    }

    [Fact]
    public async Task AServerErrorOrNetworkError_IsTriedAgain_AndA4xxIsReturned()
    {
        var api = new FakeHeroesProfileApi();
        api.Answer("flaky", Error(503), Ok("up"));
        api.Answer("gone", Error(404, "player_not_found"));
        HeroesProfileCaller caller = Caller(api);

        Assert.Equal(
            "up",
            (await caller.GetAsync("flaky", null, JobTimeout, CancellationToken.None)).Body
        );
        Assert.Contains(HeroesProfileCaller.TransientDelay, api.Delays);
        HeroesProfileGlobalAnswer missing = await caller.GetAsync(
            "gone",
            null,
            JobTimeout,
            CancellationToken.None
        );
        Assert.Equal(404, missing.StatusCode);
        Assert.Equal("player_not_found", missing.ErrorCode);

        api.Throw = new HttpRequestException("offline");
        await Assert.ThrowsAsync<HeroStatsException>(() =>
            caller.GetAsync("offline", null, JobTimeout, CancellationToken.None)
        );
        Assert.Equal(
            HeroesProfileCaller.MaxTransient + 1,
            api.Calls.Count(call => call.Path == "offline")
        );
    }

    [Fact]
    public async Task AJobPastItsTimeout_Fails()
    {
        var api = new FakeHeroesProfileApi();
        api.Answer("slow", Accepted("job-slow", TimeSpan.FromMinutes(2)));
        api.Job("job-slow", Accepted("job-slow", TimeSpan.FromMinutes(2)));

        HeroStatsException failed = await Assert.ThrowsAsync<HeroStatsException>(() =>
            Caller(api).GetAsync("slow", null, TimeSpan.FromMinutes(5), CancellationToken.None)
        );

        Assert.Equal(HeroStatsFailure.Unavailable, failed.Failure);
        Assert.Contains("did not finish", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSharedPacer_SpacesEveryRequest_AndALaneKeepsItsOwnGap()
    {
        var api = new FakeHeroesProfileApi();
        api.Answer("x", Ok("x"));
        HeroesProfilePacer pacer = api.Pacer();
        var one = new HeroesProfileCaller(api, pacer, NullLogger.Instance, api.Clock, api.Delay);
        var two = new HeroesProfileCaller(api, pacer, NullLogger.Instance, api.Clock, api.Delay);

        await one.GetAsync(
            "x",
            null,
            JobTimeout,
            CancellationToken.None,
            "group_by_map",
            TimeSpan.FromSeconds(61)
        );
        await two.GetAsync("x", null, JobTimeout, CancellationToken.None);
        await one.GetAsync(
            "x",
            null,
            JobTimeout,
            CancellationToken.None,
            "group_by_map",
            TimeSpan.FromSeconds(61)
        );

        Assert.Equal(TimeSpan.FromSeconds(1.1), api.Calls[1].At - api.Calls[0].At);
        Assert.Equal(TimeSpan.FromSeconds(61), api.Calls[2].At - api.Calls[0].At);
    }

    [Fact]
    public void TheGlobalAnswer_KeepsTheQuotaHeaders()
    {
        HeroesProfileGlobalAnswer answer = HeroesProfileGlobalAnswer.From(
            200,
            "[]",
            null,
            null,
            49999,
            54
        );

        Assert.Equal(49999, answer.QuotaRemaining);
        Assert.Equal(54, answer.RateLimitRemaining);
        Assert.Null(HeroesProfileGlobalAnswer.From(200, "[]", null).QuotaRemaining);
    }

    private static HeroesProfileCaller Caller(FakeHeroesProfileApi api) =>
        new(api, api.Pacer(), NullLogger.Instance, api.Clock, api.Delay);
}
