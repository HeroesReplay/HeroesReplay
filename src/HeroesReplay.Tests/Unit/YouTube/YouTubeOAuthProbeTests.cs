using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.YouTube;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

/// <summary>
/// #305: the youtube role's probe refreshes the stored upload token at Google's token endpoint:
/// no YouTube Data API call, so no quota unit and no upload call.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeOAuthProbeTests
{
    private const string RefreshToken = "1//refresh-token-do-not-print";
    private const string ClientSecret = "client-secret-do-not-print";

    [Theory]
    [InlineData(false, false, "YouTube:Enabled is false")]
    [InlineData(true, true, "YouTube:DryRun is on")]
    public void AnUploaderThatDoesNotCallYouTube_DoesNotUseTheProbe(
        bool enabled,
        bool dryRun,
        string reason
    )
    {
        var probe = new FakeGoogle().Probe(
            new YouTubeSettings
            {
                Enabled = enabled,
                DryRun = dryRun,
                ChannelId = "UC1",
            }
        );

        Assert.Contains(reason, probe.NotUsedReason);
    }

    [Fact]
    public async Task ARefreshedToken_IsOk_AndSendsTheStoredRefreshToken()
    {
        var google = new FakeGoogle();

        ServiceDependencyResult result = await google.Probe().CheckAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Ok, result.State);
        Assert.Contains("no YouTube quota spent", result.Cause);
        RefreshTokenRequest sent = Assert.Single(google.Sent);
        Assert.Equal(RefreshToken, sent.RefreshToken);
        Assert.Equal("client-id", sent.ClientId);
        Assert.Equal("refresh_token", sent.GrantType);
        Assert.DoesNotContain(RefreshToken, result.Cause);
    }

    [Fact]
    public async Task ARevokedRefreshToken_IsInvalid_WithTheConsentFix()
    {
        var google = new FakeGoogle
        {
            Failure = new TokenResponseException(
                new TokenErrorResponse
                {
                    Error = "invalid_grant",
                    ErrorDescription = "Token has been expired or revoked.",
                },
                HttpStatusCode.BadRequest
            ),
        };

        ServiceDependencyResult result = await google.Probe().CheckAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Rejected, result.State);
        Assert.Equal(YouTubeOAuthProbe.InvalidCode, result.Code);
        Assert.Contains("invalid_grant", result.Cause);
        Assert.Contains("expired or revoked", result.Cause);
        Assert.Contains("TokenResponse-UC1", result.Remediation);
        Assert.Contains("youtube uploader", result.Remediation);
        Assert.DoesNotContain(RefreshToken, result.Cause + result.Remediation);
        Assert.DoesNotContain(ClientSecret, result.Cause + result.Remediation);
    }

    [Fact]
    public async Task NoStoredConsent_IsMissing_WithoutACall()
    {
        var google = new FakeGoogle { Stored = null };

        ServiceDependencyResult result = await google.Probe().CheckAsync(CancellationToken.None);

        Assert.Equal(YouTubeOAuthProbe.MissingCode, result.Code);
        Assert.Equal(ServiceDependencyStates.Rejected, result.State);
        Assert.Empty(google.Sent);
    }

    [Fact]
    public async Task NoClientSecrets_IsMissing_WithoutACall()
    {
        var google = new FakeGoogle { Secrets = null };

        ServiceDependencyResult result = await google.Probe().CheckAsync(CancellationToken.None);

        Assert.Equal(YouTubeOAuthProbe.MissingCode, result.Code);
        Assert.Contains("client_secrets.json", result.Cause);
        Assert.Empty(google.Sent);
    }

    [Fact]
    public async Task NoNetwork_IsUnreachable()
    {
        var google = new FakeGoogle
        {
            Failure = new HttpRequestException(
                "No such host is known. (oauth2.googleapis.com:443)"
            ),
        };

        ServiceDependencyResult result = await google.Probe().CheckAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Unreachable, result.State);
        Assert.Equal(YouTubeOAuthProbe.UnreachableCode, result.Code);
    }

    [Fact]
    public async Task AGoogleServerError_IsUnreachable_NotInvalid()
    {
        var google = new FakeGoogle
        {
            Failure = new TokenResponseException(
                new TokenErrorResponse { Error = "internal_failure" },
                HttpStatusCode.ServiceUnavailable
            ),
        };

        ServiceDependencyResult result = await google.Probe().CheckAsync(CancellationToken.None);

        Assert.Equal(YouTubeOAuthProbe.UnreachableCode, result.Code);
        Assert.Contains("HTTP 503", result.Cause);
    }

    private sealed class FakeGoogle
    {
        public TokenResponse Stored { get; init; } =
            new() { RefreshToken = RefreshToken, AccessToken = "ya29.old" };

        public ClientSecrets Secrets { get; init; } =
            new() { ClientId = "client-id", ClientSecret = ClientSecret };

        public Exception Failure { get; init; }
        public List<RefreshTokenRequest> Sent { get; } = new();

        public YouTubeOAuthProbe Probe(YouTubeSettings settings = null) =>
            new(
                settings
                    ?? new YouTubeSettings
                    {
                        Enabled = true,
                        DryRun = false,
                        ChannelId = "UC1",
                    },
                _ => Task.FromResult(Stored),
                () => Secrets,
                (request, _) =>
                {
                    Sent.Add(request);
                    return Failure != null
                        ? Task.FromException<TokenResponse>(Failure)
                        : Task.FromResult(new TokenResponse { AccessToken = "ya29.new" });
                }
            );
    }
}
