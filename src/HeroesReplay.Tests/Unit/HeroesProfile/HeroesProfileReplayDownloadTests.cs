using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.HeroesProfile.Client;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

/// <summary>
/// #361: the replay download keeps the status and the error body's <c>error.code</c>, and nothing
/// else from the body. Over a fake HTTP handler, no network.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesProfileReplayDownloadTests
{
    [Fact]
    public async Task ASuccessfulDownload_CopiesTheReplay()
    {
        var handler = new OneAnswer(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 7, 8, 9 }),
            }
        );
        using var destination = new MemoryStream();

        await Client(handler).DownloadReplayAsync(65625279, destination);

        Assert.Equal(new byte[] { 7, 8, 9 }, destination.ToArray());
        Assert.Contains("replayID=65625279", handler.Query, StringComparison.Ordinal);
        Assert.Equal("Bearer", handler.Scheme);
    }

    [Theory]
    [InlineData(
        HttpStatusCode.Forbidden,
        """{"error":{"code":"replay_deleted","message":"That replay is no longer stored.","endpoint":"replay_download"}}""",
        "replay_deleted"
    )]
    [InlineData(
        HttpStatusCode.Forbidden,
        """{"error":{"code":"endpoint_not_in_plan","message":"Upgrade."}}""",
        "endpoint_not_in_plan"
    )]
    [InlineData(
        HttpStatusCode.Unauthorized,
        """{"error":{"code":"invalid_api_key","message":"Bad key."}}""",
        "invalid_api_key"
    )]
    [InlineData(
        HttpStatusCode.NotFound,
        """{"error":{"code":"replay_not_found"}}""",
        "replay_not_found"
    )]
    [InlineData(HttpStatusCode.Forbidden, "<html>Forbidden</html>", null)]
    [InlineData(HttpStatusCode.Forbidden, "", null)]
    public async Task AnErrorAnswer_ThrowsItsStatusAndCode_AndNoBody(
        HttpStatusCode status,
        string body,
        string code
    )
    {
        var handler = new OneAnswer(
            new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            }
        );
        using var destination = new MemoryStream();

        HeroesProfileApiException error = await Assert.ThrowsAsync<HeroesProfileApiException>(() =>
            Client(handler).DownloadReplayAsync(1, destination)
        );

        Assert.Equal((int)status, error.ResponseStatusCode);
        Assert.Equal(code, error.ErrorCode);
        Assert.Equal(0, destination.Length);
        Assert.DoesNotContain("message", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stored", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("test-key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptySuccess_IsNotAReplay()
    {
        var handler = new OneAnswer(new HttpResponseMessage(HttpStatusCode.NoContent));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Client(handler).DownloadReplayAsync(1, new MemoryStream())
        );
    }

    [Theory]
    [InlineData("""{"error":{"code":"replay_deleted"}}""", "replay_deleted")]
    [InlineData("""{"error":{"code":"  rate_limited  "}}""", "rate_limited")]
    [InlineData("""{"error":{"code":"bad code\nwith a newline"}}""", null)]
    [InlineData("""{"error":{"code":"key=abc123 Bearer x"}}""", null)]
    [InlineData("""{"error":{"code":42}}""", null)]
    [InlineData("""{"error":"replay_deleted"}""", null)]
    [InlineData("""[{"error":{"code":"replay_deleted"}}]""", null)]
    [InlineData("{\"error\":{\"code\":\"replay_deleted", null)]
    [InlineData(null, null)]
    public void Code_TakesOnlyAShortToken(string body, string expected)
    {
        Assert.Equal(expected, HeroesProfileErrorBody.Code(body));
    }

    [Fact]
    public void Code_RejectsALongCode()
    {
        string code = new('a', 65);

        Assert.Null(HeroesProfileErrorBody.Code("{\"error\":{\"code\":\"" + code + "\"}}"));
    }

    /// <summary>An error body is read up to a bound, never to its end.</summary>
    [Fact]
    public async Task ReadCodeAsync_StopsAtTheBound()
    {
        var body = new string(' ', HeroesProfileErrorBody.MaxBytes) + """{"error":{"code":"x"}}""";
        using var content = new StringContent(body);

        Assert.Null(await HeroesProfileErrorBody.ReadCodeAsync(content, CancellationToken.None));
    }

    private static HeroesProfileClient Client(HttpMessageHandler handler) =>
        HeroesProfileClientFactory.Create(
            "test-key",
            new HttpClient(handler),
            new Uri("https://www.heroesprofile.com/api/external/v1/")
        );

    private sealed class OneAnswer(HttpResponseMessage answer) : HttpMessageHandler
    {
        public string Query { get; private set; }

        public string Scheme { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Query = request.RequestUri?.Query;
            Scheme = request.Headers.Authorization?.Scheme;
            return Task.FromResult(answer);
        }
    }
}
