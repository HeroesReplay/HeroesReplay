using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.Core.TwitchExtension;
using Xunit;

namespace HeroesReplay.Tests.Unit.Check;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class TwitchExtensionCheckTests
{
    [Fact]
    public void DisabledExtensionPassesWithoutAnUploaderKey()
    {
        CheckCommand.CheckResult result = CheckCommand.TwitchExtensionDisabled(enabled: false);

        Assert.True(result.Ok);
        Assert.Equal("twitch-extension", result.Name);
        Assert.Equal("Twitch extension is disabled.", result.Detail);
        Assert.Equal("check.twitch_extension.disabled", result.Code);
    }

    [Fact]
    public void EnabledExtensionStillRequiresWhoAmI()
    {
        Assert.Null(CheckCommand.TwitchExtensionDisabled(enabled: true));
    }

    [Fact]
    public void RejectedUploaderKeyFailsWithTheServerMessage()
    {
        CheckCommand.CheckResult result = CheckCommand.TwitchExtensionWhoAmI(
            new ExtensionWhoAmI
            {
                Reachable = false,
                StatusCode = 401,
                Message =
                    "This uploader key is not valid. Create a new one at heroesprofile.com/Api/Account.",
            }
        );

        Assert.False(result.Ok);
        Assert.Equal("twitch-extension", result.Name);
        Assert.Equal(
            "This uploader key is not valid. Create a new one at heroesprofile.com/Api/Account.",
            result.Detail
        );
        Assert.Equal("check.twitch_extension.key_rejected", result.Code);
    }

    [Theory]
    [InlineData(401, "check.twitch_extension.key_rejected")]
    [InlineData(403, "check.twitch_extension.key_rejected")]
    [InlineData(429, "check.twitch_extension.rate_limited")]
    [InlineData(0, "check.twitch_extension.unreachable")]
    [InlineData(502, "check.twitch_extension.http_error")]
    public void AFailedWhoAmI_HasAStableCodePerStatus(int status, string code)
    {
        CheckCommand.CheckResult result = CheckCommand.TwitchExtensionWhoAmI(
            new ExtensionWhoAmI
            {
                Reachable = false,
                StatusCode = status,
                Message = "no",
            }
        );

        Assert.False(result.Ok);
        Assert.Equal(code, result.Code);
        Assert.Contains(code, CheckCodes.All);
    }

    [Fact]
    public void NoWhoAmIAtAll_IsUnreachable()
    {
        Assert.Equal(
            CheckCodes.TwitchExtensionUnreachable,
            CheckCommand.TwitchExtensionWhoAmI(null).Code
        );
    }

    [Theory]
    [InlineData(true, "entitlement.active=True")]
    [InlineData(false, "entitlement.active=False")]
    public void AcceptedUploaderKeyReportsTheChannelAndEntitlement(bool active, string expected)
    {
        CheckCommand.CheckResult result = CheckCommand.TwitchExtensionWhoAmI(
            new ExtensionWhoAmI
            {
                Reachable = true,
                StatusCode = 200,
                TwitchLogin = "saltysadism",
                TwitchDisplayName = "SaltySadism",
                EntitlementActive = active,
            }
        );

        Assert.True(result.Ok);
        Assert.StartsWith("Connected to SaltySadism.", result.Detail);
        Assert.Contains(expected, result.Detail);
        Assert.Equal(CheckCodes.TwitchExtensionOk, result.Code);
    }
}
