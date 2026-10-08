using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using HeroesReplay.Core.GameClient.Firewall;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient.Firewall;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class NetshGameFirewallTests
{
    private const string Exe =
        @"C:\Program Files (x86)\Heroes of the Storm\Versions\Base98304\HeroesOfTheStorm_x64.exe";

    private const string OtherExe =
        @"C:\Program Files (x86)\Heroes of the Storm\Versions\Base98348\HeroesOfTheStorm_x64.exe";

    private const string OwnName = "HeroesReplay inbound Base98304";

    [Fact]
    public void Unelevated_MissingRule_IsReportedAndNotAdded()
    {
        var netsh = new FakeNetsh();
        var firewall = Firewall(elevated: false, netsh);

        IReadOnlyList<FirewallRuleOutcome> outcomes = firewall.AllowInboundClients(new[] { Exe });

        Assert.Equal(FirewallRuleState.MissingNeedsElevation, Assert.Single(outcomes).State);
        Assert.DoesNotContain(netsh.Calls, call => call.Contains("add rule"));
    }

    [Fact]
    public void Unelevated_OwnRule_IsAlreadyAllowed()
    {
        var netsh = new FakeNetsh { Shown = Show(Rule(OwnName, Exe)) };
        var firewall = Firewall(elevated: false, netsh);

        IReadOnlyList<FirewallRuleOutcome> outcomes = firewall.AllowInboundClients(new[] { Exe });

        Assert.Equal(FirewallRuleState.AlreadyAllowed, Assert.Single(outcomes).State);
        Assert.Equal(NetshGameFirewall.ShowInboundArguments, Assert.Single(netsh.Calls));
    }

    [Fact]
    public void LowerCaseProgramPath_CountsAsAllowed()
    {
        var netsh = new FakeNetsh
        {
            Shown = Show(Rule("Heroes of the Storm", Exe.ToLowerInvariant(), "Private,Public")),
        };
        var firewall = Firewall(elevated: false, netsh);

        FirewallRuleOutcome outcome = Assert.Single(firewall.AllowInboundClients(new[] { Exe }));

        Assert.Equal(FirewallRuleState.AlreadyAllowed, outcome.State);
        Assert.Equal(Exe, outcome.ProgramPath);
    }

    [Fact]
    public void ProgramPathWithEnvironmentVariable_CountsAsAllowed()
    {
        string drive = Environment.ExpandEnvironmentVariables("%SystemDrive%");
        string exe =
            drive + @"\Games\Heroes of the Storm\Versions\Base98304\HeroesOfTheStorm_x64.exe";
        var netsh = new FakeNetsh
        {
            Shown = Show(
                Rule(
                    OwnName,
                    @"%SystemDrive%\Games\Heroes of the Storm\Versions\.\Base98304\HeroesOfTheStorm_x64.exe"
                )
            ),
        };
        var firewall = Firewall(elevated: false, netsh);

        Assert.Equal(
            FirewallRuleState.AlreadyAllowed,
            Assert.Single(firewall.AllowInboundClients(new[] { exe })).State
        );
    }

    [Theory]
    [InlineData("Heroes of the Storm")]
    [InlineData(
        @"TCP Query User{F334C9D9-EF16-4A17-BDD5-17E9C64DC235}c:\program files (x86)\heroes of the storm\versions\base98304\heroesofthestorm_x64.exe"
    )]
    public void QueryUserRule_CountsAndElevatedAddsNoDuplicate(string name)
    {
        // Windows adds a TCP and a UDP rule, in lower case, for the profile the operator allowed.
        string program = Exe.ToLowerInvariant();
        var netsh = new FakeNetsh
        {
            Shown = Show(
                Rule(name, program, "Private", protocol: "TCP"),
                Rule(name, program, "Private", protocol: "UDP")
            ),
        };
        var firewall = Firewall(elevated: true, netsh);

        FirewallRuleOutcome outcome = Assert.Single(firewall.AllowInboundClients(new[] { Exe }));

        Assert.Equal(FirewallRuleState.AlreadyAllowed, outcome.State);
        Assert.Equal("\"" + name + "\" (Private)", outcome.AllowedBy);
        Assert.Equal(NetshGameFirewall.ShowInboundArguments, Assert.Single(netsh.Calls));
    }

    [Fact]
    public void RuleForSomeProfiles_CountsAndTheLogNamesItsProfiles()
    {
        var logger = new ListLogger();
        var netsh = new FakeNetsh
        {
            Shown = Show(Rule("Heroes of the Storm", Exe.ToLowerInvariant(), "Private")),
        };
        var firewall = Firewall(elevated: false, netsh, logger);

        Assert.Equal(
            FirewallRuleState.AlreadyAllowed,
            Assert.Single(firewall.AllowInboundClients(new[] { Exe })).State
        );

        LogEntry entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.Contains("\"Heroes of the Storm\" (Private)", entry.Message);
        Assert.Contains("cover only these profiles: Private.", entry.Message);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public void DisabledRule_DoesNotCount()
    {
        var netsh = new FakeNetsh { Shown = Show(Rule(OwnName, Exe, enabled: "No")) };
        var firewall = Firewall(elevated: false, netsh);

        Assert.Equal(
            FirewallRuleState.MissingNeedsElevation,
            Assert.Single(firewall.AllowInboundClients(new[] { Exe })).State
        );
    }

    [Fact]
    public void BlockRule_DoesNotCount()
    {
        var netsh = new FakeNetsh
        {
            Shown = Show(Rule("Heroes of the Storm", Exe.ToLowerInvariant(), action: "Block")),
        };
        var firewall = Firewall(elevated: false, netsh);

        Assert.Equal(
            FirewallRuleState.MissingNeedsElevation,
            Assert.Single(firewall.AllowInboundClients(new[] { Exe })).State
        );
    }

    [Fact]
    public void OutboundRule_DoesNotCount()
    {
        var netsh = new FakeNetsh { Shown = Show(Rule(OwnName, Exe, direction: "Out")) };
        var firewall = Firewall(elevated: false, netsh);

        Assert.Equal(
            FirewallRuleState.MissingNeedsElevation,
            Assert.Single(firewall.AllowInboundClients(new[] { Exe })).State
        );
    }

    [Fact]
    public void RuleForAnotherExe_DoesNotCount()
    {
        var netsh = new FakeNetsh { Shown = Show(Rule("Heroes of the Storm", OtherExe)) };
        var firewall = Firewall(elevated: false, netsh);

        Assert.Equal(
            FirewallRuleState.MissingNeedsElevation,
            Assert.Single(firewall.AllowInboundClients(new[] { Exe })).State
        );
    }

    [Fact]
    public void MissingRule_IsWarnedOncePerExeAcrossLaunches()
    {
        var logger = new ListLogger();
        var netsh = new FakeNetsh();
        var firewall = Firewall(elevated: false, netsh, logger);

        firewall.AllowInboundClients(new[] { Exe });
        IReadOnlyList<FirewallRuleOutcome> second = firewall.AllowInboundClients(new[] { Exe });

        Assert.Equal(FirewallRuleState.MissingNeedsElevation, Assert.Single(second).State);
        LogEntry warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(Exe, warning.Message);

        // Another exe is still warned about the first time it is checked.
        firewall.AllowInboundClients(new[] { Exe, OtherExe });
        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Warning));
        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains(OtherExe)
        );
    }

    [Fact]
    public void AllowedExe_IsLoggedAtInformationOncePerProcess()
    {
        var logger = new ListLogger();
        var netsh = new FakeNetsh { Shown = Show(Rule(OwnName, Exe)) };
        var firewall = Firewall(elevated: false, netsh, logger);

        firewall.AllowInboundClients(new[] { Exe });
        firewall.AllowInboundClients(new[] { Exe });

        LogEntry entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.Contains("\"" + OwnName + "\" (Domain,Private,Public)", entry.Message);
    }

    [Fact]
    public void Elevated_MissingRule_IsAdded()
    {
        var netsh = new FakeNetsh();
        var firewall = Firewall(elevated: true, netsh);

        IReadOnlyList<FirewallRuleOutcome> outcomes = firewall.AllowInboundClients(new[] { Exe });

        Assert.Equal(FirewallRuleState.Added, Assert.Single(outcomes).State);
        Assert.Contains(
            netsh.Calls,
            call => call.Contains("add rule") && call.Contains(OwnName) && call.Contains(Exe)
        );
    }

    [Fact]
    public void Elevated_OnlyDisabledOrBlockRules_AddTheNamedRule()
    {
        var netsh = new FakeNetsh
        {
            Shown = Show(
                Rule("Heroes of the Storm", Exe.ToLowerInvariant(), action: "Block"),
                Rule(OwnName, Exe, enabled: "No")
            ),
        };
        var firewall = Firewall(elevated: true, netsh);

        Assert.Equal(
            FirewallRuleState.Added,
            Assert.Single(firewall.AllowInboundClients(new[] { Exe })).State
        );
        Assert.Single(netsh.Calls, call => call.Contains("add rule"));
    }

    [Fact]
    public void Elevated_AddFailure_IsFailed()
    {
        var netsh = new FakeNetsh { AddCode = 1 };
        var firewall = Firewall(elevated: true, netsh);

        IReadOnlyList<FirewallRuleOutcome> outcomes = firewall.AllowInboundClients(new[] { Exe });

        Assert.Equal(FirewallRuleState.Failed, Assert.Single(outcomes).State);
    }

    [Fact]
    public void NetshThatDoesNotStart_FailsEveryExe()
    {
        var firewall = new NetshGameFirewall(
            new ListLogger(),
            () => false,
            (arguments, capture) => throw new Win32Exception(2)
        );

        IReadOnlyList<FirewallRuleOutcome> outcomes = firewall.AllowInboundClients(
            new[] { Exe, OtherExe }
        );

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, outcome => Assert.Equal(FirewallRuleState.Failed, outcome.State));
    }

    [Fact]
    public void Elevated_DuplicateRules_AreReplacedByOne()
    {
        string one = Rule(OwnName, Exe);
        var netsh = new FakeNetsh { Shown = Show(one, one, one) };
        var firewall = Firewall(elevated: true, netsh);

        IReadOnlyList<FirewallRuleOutcome> outcomes = firewall.AllowInboundClients(new[] { Exe });

        Assert.Equal(FirewallRuleState.Added, Assert.Single(outcomes).State);
        Assert.Contains(netsh.Calls, call => call.Contains("delete rule"));
        Assert.Single(netsh.Calls, call => call.Contains("add rule"));
    }

    [Fact]
    public void Unelevated_DuplicateRules_AreLeftAndCountAsAllowed()
    {
        string one = Rule(OwnName, Exe);
        var netsh = new FakeNetsh { Shown = Show(one, one) };
        var firewall = Firewall(elevated: false, netsh);

        Assert.Equal(
            FirewallRuleState.AlreadyAllowed,
            Assert.Single(firewall.AllowInboundClients(new[] { Exe })).State
        );
        Assert.DoesNotContain(netsh.Calls, call => call.Contains("delete") || call.Contains("add"));
    }

    [Fact]
    public void ParseRules_ReadsEachVerboseRule()
    {
        IReadOnlyList<NetshGameFirewall.ShownRule> rules = NetshGameFirewall.ParseRules(
            Show(
                Rule("Heroes of the Storm", Exe.ToLowerInvariant(), "Private,Public"),
                Icmp("Core Networking - Destination Unreachable (ICMPv6-In)"),
                Rule(OwnName, Exe, enabled: "No", direction: "Out", action: "Block")
            )
        );

        Assert.Equal(3, rules.Count);
        Assert.Equal(
            new NetshGameFirewall.ShownRule(
                "Heroes of the Storm",
                "Yes",
                "In",
                "Private,Public",
                "Allow",
                Exe.ToLowerInvariant()
            ),
            rules[0]
        );
        Assert.True(rules[0].AllowsInbound);
        Assert.Equal("Allow", rules[1].Action);
        Assert.Equal(string.Empty, rules[1].Program);

        // "Ok." follows the last rule's Action line and is not part of it.
        Assert.Equal(
            new NetshGameFirewall.ShownRule(
                OwnName,
                "No",
                "Out",
                "Domain,Private,Public",
                "Block",
                Exe
            ),
            rules[2]
        );
        Assert.False(rules[2].AllowsInbound);
    }

    [Fact]
    public void ParseRules_ReadsTheWrappedProgramLine()
    {
        const string verbose =
            "Rule Name:                            HeroesReplay inbound Base98304\r\n"
            + "----------------------------------------------------------------------\r\n"
            + "Enabled:                              Yes\r\n"
            + "Direction:                            In\r\n"
            + @"Program:                              C:\Program Files (x86)\Heroes of the "
            + "\r\n"
            + @"Storm\Versions\Base98304\HeroesOfTheStorm_x64.exe"
            + "\r\n"
            + "Action:                               Allow\r\n";

        NetshGameFirewall.ShownRule rule = Assert.Single(NetshGameFirewall.ParseRules(verbose));

        Assert.Equal(Exe, rule.Program);
        Assert.Single(NetshGameFirewall.AllowingRules(new[] { rule }, Exe));
    }

    [Fact]
    public void ParseRules_NoRulesText_IsEmpty()
    {
        Assert.Empty(NetshGameFirewall.ParseRules("No rules match the specified criteria.\r\n"));
        Assert.Empty(NetshGameFirewall.ParseRules(null));
    }

    [Fact]
    public void ShowInboundArguments_ReadEveryInboundRuleVerbose()
    {
        Assert.Equal(
            "advfirewall firewall show rule name=all dir=in verbose",
            NetshGameFirewall.ShowInboundArguments
        );
    }

    private static NetshGameFirewall Firewall(
        bool elevated,
        FakeNetsh netsh,
        ILogger<NetshGameFirewall> logger = null
    ) => new(logger ?? new ListLogger(), () => elevated, netsh.Run);

    /// <summary>Whole <c>netsh ... show rule name=all dir=in verbose</c> output.</summary>
    private static string Show(params string[] rules) => string.Concat(rules) + "Ok.\r\n\r\n";

    /// <summary>One rule in the layout netsh prints on Windows 11.</summary>
    private static string Rule(
        string name,
        string program,
        string profiles = "Domain,Private,Public",
        string enabled = "Yes",
        string direction = "In",
        string action = "Allow",
        string protocol = "Any"
    ) =>
        "\r\n"
        + Line("Rule Name", name)
        + "----------------------------------------------------------------------\r\n"
        + Line("Enabled", enabled)
        + Line("Direction", direction)
        + Line("Profiles", profiles)
        + Line("Grouping", string.Empty)
        + Line("LocalIP", "Any")
        + Line("RemoteIP", "Any")
        + Line("Protocol", protocol)
        + Line("Edge traversal", "Defer to user")
        + Line("Program", program)
        + Line("InterfaceTypes", "Any")
        + Line("Security", "NotRequired")
        + Line("Rule source", "Local Setting")
        + Line("Action", action);

    private static string Icmp(string name) =>
        "\r\n"
        + Line("Rule Name", name)
        + "----------------------------------------------------------------------\r\n"
        + Line("Enabled", "Yes")
        + Line("Direction", "In")
        + Line("Profiles", "Domain,Private,Public")
        + Line("Protocol", "ICMPv6")
        + "                                      Type    Code\r\n"
        + "                                      1       Any \r\n"
        + Line("Action", "Allow");

    private static string Line(string key, string value) =>
        (key + ":").PadRight(38) + value + "\r\n";

    private sealed class FakeNetsh
    {
        public List<string> Calls { get; } = new();

        /// <summary>Rules netsh shows; null is "No rules match" (exit 1).</summary>
        public string Shown { get; init; }

        public int AddCode { get; init; }

        public NetshGameFirewall.NetshResult Run(string arguments, bool capture)
        {
            Calls.Add(arguments);
            if (arguments.Contains("show rule"))
            {
                return Shown == null
                    ? new NetshGameFirewall.NetshResult(
                        1,
                        "\r\nNo rules match the specified criteria.\r\n"
                    )
                    : new NetshGameFirewall.NetshResult(0, Shown);
            }

            return new NetshGameFirewall.NetshResult(
                arguments.Contains("add rule") ? AddCode : 0,
                string.Empty
            );
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class ListLogger : ILogger<NetshGameFirewall>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
