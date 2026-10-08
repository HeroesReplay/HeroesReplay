using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Twitch;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

/// <summary>#305: the twitch role's probe is Twitch's token validator.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class TwitchTokenProbeTests
{
    private const string Token = "twitch-access-token-do-not-print";

    [Fact]
    public void ARoleWithChatRedemptionsAndPredictionsOff_DoesNotUseTheProbe()
    {
        var probe = new TwitchTokenProbe(
            new TwitchSettings { AccessToken = Token },
            (_, _) => Task.FromResult(new TwitchTokenValidation(200, "chat:read"))
        );

        Assert.Contains("are off", probe.NotUsedReason);
    }

    [Fact]
    public async Task AValidToken_IsOk()
    {
        ServiceDependencyResult result = await Probe(
                new TwitchTokenValidation(200, "chat:read chat:edit")
            )
            .CheckAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Ok, result.State);
        Assert.Contains("2 scope(s)", result.Cause);
    }

    [Fact]
    public async Task ARejectedToken_IsInvalid()
    {
        ServiceDependencyResult result = await Probe(new TwitchTokenValidation(401, null))
            .CheckAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Rejected, result.State);
        Assert.Equal(TwitchTokenProbe.InvalidCode, result.Code);
        Assert.Contains("HTTP 401", result.Cause);
        Assert.Contains("check twitch", result.Remediation);
        Assert.DoesNotContain(Token, result.Cause + result.Remediation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(500)]
    [InlineData(429)]
    public async Task NoAnswerOrAServerError_IsUnreachable(int? status)
    {
        ServiceDependencyResult result = await Probe(new TwitchTokenValidation(status, null))
            .CheckAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Unreachable, result.State);
        Assert.Equal(TwitchTokenProbe.UnreachableCode, result.Code);
    }

    private static TwitchTokenProbe Probe(TwitchTokenValidation answer) =>
        new(
            new TwitchSettings { AccessToken = Token, EnableChatBot = true },
            (token, _) =>
            {
                Assert.Equal(Token, token);
                return Task.FromResult(answer);
            }
        );
}
