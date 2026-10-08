using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.HeroesProfile.Client;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

/// <summary>The hand-written global statistics calls on the Kiota client (<c>GlobalStats.cs</c>).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesProfileGlobalStatsTests
{
    private static readonly Uri Base = new Uri("https://www.heroesprofile.com/api/external/v1/");

    [Fact]
    public async Task GetGlobal_SendsTheQueryWithTheBearerKeyAndReadsA202()
    {
        var handler = new Recorder(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(
                    "{\"async\":true,\"status\":\"pending\",\"job_id\":\"07bd602f\"}",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(10));
            return response;
        });
        HeroesProfileClient client = HeroesProfileClientFactory.Create(
            "test-key",
            new HttpClient(handler),
            Base
        );

        HeroesProfileGlobalAnswer answer = await client.GetGlobalAsync(
            "heroes/matchups",
            new[]
            {
                new KeyValuePair<string, string>("timeframe_type", "major"),
                new KeyValuePair<string, string>("timeframe", "2.57"),
                new KeyValuePair<string, string>("game_type", "sl"),
                new KeyValuePair<string, string>("hero", "Xal'atath"),
                new KeyValuePair<string, string>("region", null),
            }
        );

        Assert.Equal(202, answer.StatusCode);
        Assert.Equal("07bd602f", answer.JobId);
        Assert.Equal(TimeSpan.FromSeconds(10), answer.RetryAfter);
        HttpRequestMessage sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal(
            "https://www.heroesprofile.com/api/external/v1/heroes/matchups?timeframe_type=major&timeframe=2.57&game_type=sl&hero=Xal%27atath",
            sent.RequestUri.AbsoluteUri
        );
        Assert.Equal("Bearer", sent.Headers.Authorization?.Scheme);
        Assert.Equal("test-key", sent.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task GetJob_PollsTheJobPathAndReturnsTheBody()
    {
        var handler = new Recorder(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"ally\":[],\"enemy\":[]}",
                Encoding.UTF8,
                "application/json"
            ),
        });
        HeroesProfileClient client = HeroesProfileClientFactory.Create(
            "test-key",
            new HttpClient(handler),
            Base
        );

        HeroesProfileGlobalAnswer answer = await client.GetJobAsync("07bd602f-4647");

        Assert.Equal(200, answer.StatusCode);
        Assert.Equal("{\"ally\":[],\"enemy\":[]}", answer.Body);
        Assert.Null(answer.JobId);
        Assert.Equal(
            "https://www.heroesprofile.com/api/external/v1/jobs/07bd602f-4647",
            Assert.Single(handler.Requests).RequestUri.AbsoluteUri
        );
    }

    [Fact]
    public async Task GetGlobal_ReturnsAnErrorStatusInsteadOfThrowing()
    {
        var handler = new Recorder(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent(
                    "{\"error\":{\"code\":\"rate_limited\",\"message\":\"Too Many Attempts.\"}}",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return response;
        });
        HeroesProfileClient client = HeroesProfileClientFactory.Create(
            "test-key",
            new HttpClient(handler),
            Base
        );

        HeroesProfileGlobalAnswer answer = await client.GetGlobalAsync("heroes/stats", null);

        Assert.Equal(429, answer.StatusCode);
        Assert.Equal("rate_limited", answer.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(1), answer.RetryAfter);
    }

    [Fact]
    public void Answer_TakesTheHeaderJobIdOverTheBody()
    {
        HeroesProfileGlobalAnswer answer = HeroesProfileGlobalAnswer.From(
            202,
            "{\"job_id\":\"from-body\"}",
            null,
            "from-header"
        );

        Assert.Equal("from-header", answer.JobId);
        Assert.Null(HeroesProfileGlobalAnswer.From(500, "<html>", null).ErrorCode);
    }

    private sealed class Recorder : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;

        public Recorder(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            this.respond = respond;
        }

        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
