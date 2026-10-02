using System.Collections.Generic;
using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesFirewallConsentTests
{
    [Fact]
    public void TryForClient_NamesTheRuleForThatBaseFolder()
    {
        string exe = @"C:\HotS\Versions\Base98304\HeroesOfTheStorm_x64.exe";

        bool created = HeroesFirewallConsent.TryForClient(exe, out HeroesFirewallRule rule);

        Assert.True(created);
        Assert.Equal("HeroesReplay inbound Base98304", rule.Name);
        Assert.EndsWith(@"Base98304\HeroesOfTheStorm_x64.exe", rule.ProgramPath);
    }

    [Fact]
    public void TryForClient_IgnoresTheSwitcher()
    {
        bool created = HeroesFirewallConsent.TryForClient(
            @"C:\HotS\Support64\HeroesSwitcher_x64.exe",
            out _
        );

        Assert.False(created);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryForClient_IgnoresABlankPath(string exePath)
    {
        Assert.False(HeroesFirewallConsent.TryForClient(exePath, out _));
    }

    [Fact]
    public void ForClients_KeepsEachInstalledExeOnce()
    {
        string current = @"C:\HotS\Versions\Base98304\HeroesOfTheStorm_x64.exe";
        string previous = @"C:\HotS\Versions\Base98025\HeroesOfTheStorm_x64.exe";

        IReadOnlyList<HeroesFirewallRule> rules = HeroesFirewallConsent.ForClients(
            new[] { current, current, previous, @"C:\HotS\Support64\HeroesSwitcher_x64.exe" }
        );

        Assert.Equal(2, rules.Count);
        Assert.Equal("HeroesReplay inbound Base98304", rules[0].Name);
        Assert.Equal("HeroesReplay inbound Base98025", rules[1].Name);
        Assert.Contains("dir=in", NetshGameFirewall.AddArguments(rules[0]));
        Assert.Contains("action=allow", NetshGameFirewall.AddArguments(rules[0]));
        Assert.Contains("profile=any", NetshGameFirewall.AddArguments(rules[0]));
        Assert.Contains("Base98304", NetshGameFirewall.AddArguments(rules[0]));
    }
}
