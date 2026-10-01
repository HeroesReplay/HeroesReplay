using HeroesReplay.CLI.Commands.Check;
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
}
