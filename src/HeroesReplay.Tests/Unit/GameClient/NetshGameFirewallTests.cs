using System.Collections.Generic;
using HeroesReplay.Core.GameClient;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class NetshGameFirewallTests
{
    private const string Exe =
        @"C:\Program Files (x86)\Heroes of the Storm\Versions\Base98304\HeroesOfTheStorm_x64.exe";

    [Fact]
    public void Unelevated_MissingRule_IsReportedAndNotAdded()
    {
        var calls = new List<string>();
        var firewall = Firewall(elevated: false, existing: false, calls);

        IReadOnlyList<FirewallRuleOutcome> outcomes = firewall.AllowInboundClients(new[] { Exe });

        Assert.Equal(FirewallRuleState.MissingNeedsElevation, Assert.Single(outcomes).State);
        Assert.DoesNotContain(calls, call => call.Contains("add rule"));
    }

    [Fact]
    public void Unelevated_ExistingRule_IsAlreadyAllowed()
    {
        var calls = new List<string>();
        var firewall = Firewall(elevated: false, existing: true, calls);

        IReadOnlyList<FirewallRuleOutcome> outcomes = firewall.AllowInboundClients(new[] { Exe });

        Assert.Equal(FirewallRuleState.AlreadyAllowed, Assert.Single(outcomes).State);
        Assert.Single(calls);
    }

    [Fact]
    public void Elevated_MissingRule_IsAdded()
    {
        var calls = new List<string>();
        var firewall = Firewall(elevated: true, existing: false, calls);

        IReadOnlyList<FirewallRuleOutcome> outcomes = firewall.AllowInboundClients(new[] { Exe });

        Assert.Equal(FirewallRuleState.Added, Assert.Single(outcomes).State);
        Assert.Contains(calls, call => call.Contains("add rule") && call.Contains("Base98304"));
    }

    [Fact]
    public void Elevated_AddFailure_IsFailed()
    {
        var firewall = new NetshGameFirewall(
            NullLogger<NetshGameFirewall>.Instance,
            () => true,
            (arguments, capture) => new NetshGameFirewall.NetshResult(1, string.Empty)
        );

        IReadOnlyList<FirewallRuleOutcome> outcomes = firewall.AllowInboundClients(new[] { Exe });

        Assert.Equal(FirewallRuleState.Failed, Assert.Single(outcomes).State);
    }

    [Fact]
    public void CountAllowRules_ReadsTheWrappedVerboseProgramLine()
    {
        const string verbose =
            "Rule Name:                            HeroesReplay inbound Base98304\r\n"
            + @"Program:                              C:\Program Files (x86)\Heroes of the "
            + "\r\n"
            + @"Storm\Versions\Base98304\HeroesOfTheStorm_x64.exe"
            + "\r\n"
            + "Action:                               Allow\r\n";

        Assert.Equal(1, NetshGameFirewall.CountAllowRules(verbose, Exe));
        Assert.Equal(2, NetshGameFirewall.CountAllowRules(verbose + verbose, Exe));
    }

    [Fact]
    public void CountAllowRules_PlainOutputWithoutProgram_IsZero()
    {
        const string plain =
            "Rule Name:                            HeroesReplay inbound Base98304\r\n"
            + "Action:                               Allow\r\n";

        Assert.Equal(0, NetshGameFirewall.CountAllowRules(plain, Exe));
    }

    [Fact]
    public void Elevated_DuplicateRules_AreReplacedByOne()
    {
        var calls = new List<string>();
        var firewall = new NetshGameFirewall(
            NullLogger<NetshGameFirewall>.Instance,
            () => true,
            (arguments, capture) =>
            {
                calls.Add(arguments);
                string one = "Program: " + Exe + "\nAction: Allow\n";
                return arguments.Contains("show rule")
                    ? new NetshGameFirewall.NetshResult(0, one + one + one)
                    : new NetshGameFirewall.NetshResult(0, string.Empty);
            }
        );

        IReadOnlyList<FirewallRuleOutcome> outcomes = firewall.AllowInboundClients(new[] { Exe });

        Assert.Equal(FirewallRuleState.Added, Assert.Single(outcomes).State);
        Assert.Contains(calls, call => call.Contains("delete rule"));
        Assert.Single(calls, call => call.Contains("add rule"));
    }

    [Fact]
    public void Unelevated_DuplicateRules_AreLeftAndCountAsAllowed()
    {
        var calls = new List<string>();
        var firewall = new NetshGameFirewall(
            NullLogger<NetshGameFirewall>.Instance,
            () => false,
            (arguments, capture) =>
            {
                calls.Add(arguments);
                string one = "Program: " + Exe + "\nAction: Allow\n";
                return new NetshGameFirewall.NetshResult(0, one + one);
            }
        );

        Assert.Equal(
            FirewallRuleState.AlreadyAllowed,
            Assert.Single(firewall.AllowInboundClients(new[] { Exe })).State
        );
        Assert.DoesNotContain(calls, call => call.Contains("delete") || call.Contains("add"));
    }

    [Fact]
    public void ShowArguments_AskForVerboseOutput()
    {
        Assert.True(HeroesFirewallConsent.TryForClient(Exe, out HeroesFirewallRule rule));
        Assert.EndsWith(" verbose", NetshGameFirewall.ShowArguments(rule));
    }

    private static NetshGameFirewall Firewall(bool elevated, bool existing, List<string> calls) =>
        new(
            NullLogger<NetshGameFirewall>.Instance,
            () => elevated,
            (arguments, capture) =>
            {
                calls.Add(arguments);
                if (arguments.Contains("show rule"))
                {
                    return existing
                        ? new NetshGameFirewall.NetshResult(
                            0,
                            "Program: " + Exe + "\nAction: Allow"
                        )
                        : new NetshGameFirewall.NetshResult(1, "No rules match.");
                }

                return new NetshGameFirewall.NetshResult(0, string.Empty);
            }
        );
}
