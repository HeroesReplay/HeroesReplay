using System.Collections.Generic;

namespace HeroesReplay.Core.GameClient.Firewall;

public enum FirewallRuleState
{
    AlreadyAllowed,
    Added,

    /// <summary>The rule is missing and this process is not elevated, so it was not added.</summary>
    MissingNeedsElevation,
    Failed,
}

public readonly record struct FirewallRuleOutcome(string ProgramPath, FirewallRuleState State);

public interface IGameFirewall
{
    IReadOnlyList<FirewallRuleOutcome> AllowInboundClients(IReadOnlyList<string> exePaths);
}
