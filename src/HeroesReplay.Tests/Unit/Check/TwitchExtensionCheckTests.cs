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
    }
}
