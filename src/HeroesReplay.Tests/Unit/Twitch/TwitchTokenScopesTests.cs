using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Twitch;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class TwitchTokenScopesTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Validated_ReturnsTheScopesAndSendsTheToken()
    {
        var handler = new Reply(
            HttpStatusCode.OK,
            """{"client_id":"c","login":"saltysadism","scopes":["chat:edit","channel:read:redemptions","channel:manage:predictions"],"expires_in":5000}"""
        );

        TwitchTokenValidation read = await TwitchTokenScopes.ReadAsync(
            "token",
            Timeout,
            CancellationToken.None,
            handler
        );

        Assert.True(read.Known);
        Assert.Equal(200, read.Status);
        Assert.Equal("chat:edit channel:read:redemptions channel:manage:predictions", read.Scopes);
        Assert.Equal("OAuth token", handler.Authorization);
    }

    [Fact]
    public async Task NoScopes_IsKnownAndEmpty()
    {
        TwitchTokenValidation read = await TwitchTokenScopes.ReadAsync(
            "token",
            Timeout,
            CancellationToken.None,
            new Reply(HttpStatusCode.OK, """{"client_id":"c"}""")
        );

        Assert.True(read.Known);
        Assert.Equal(string.Empty, read.Scopes);
    }

    [Fact]
    public async Task RefusedToken_KeepsTheStatusAndLeavesTheScopesUnknown()
    {
        TwitchTokenValidation read = await TwitchTokenScopes.ReadAsync(
            "token",
            Timeout,
            CancellationToken.None,
            new Reply(HttpStatusCode.Unauthorized, """{"status":401}""")
        );

        Assert.False(read.Known);
        Assert.Equal(401, read.Status);
    }

    [Fact]
    public async Task Unreachable_IsUnknownWithoutAStatus()
    {
        TwitchTokenValidation read = await TwitchTokenScopes.ReadAsync(
            "token",
            Timeout,
            CancellationToken.None,
            new Reply(new HttpRequestException("no route"))
        );

        Assert.False(read.Known);
        Assert.Null(read.Status);
    }

    [Fact]
    public async Task NoToken_DoesNotCallTwitch()
    {
        var handler = new Reply(HttpStatusCode.OK, "{}");

        TwitchTokenValidation read = await TwitchTokenScopes.ReadAsync(
            " ",
            Timeout,
            CancellationToken.None,
            handler
        );

        Assert.False(read.Known);
        Assert.Null(handler.Authorization);
    }

    private sealed class Reply : HttpMessageHandler
    {
        private readonly HttpStatusCode status;
        private readonly string body;
        private readonly Exception error;

        public Reply(HttpStatusCode status, string body)
        {
            this.status = status;
            this.body = body;
        }

        public Reply(Exception error)
        {
            this.error = error;
        }

        public string Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Authorization = string.Join(",", request.Headers.GetValues("Authorization"));
            if (error != null)
            {
                throw error;
            }

            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body) }
            );
        }
    }
}
