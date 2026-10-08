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

/// <param name="ProgramPath">The client exe.</param>
/// <param name="State">What the check found or did.</param>
/// <param name="AllowedBy">For <see cref="FirewallRuleState.AlreadyAllowed"/>: the rules that allow the exe and their profiles.</param>
public readonly record struct FirewallRuleOutcome(
    string ProgramPath,
    FirewallRuleState State,
    string AllowedBy = null
);

public interface IGameFirewall
{
    IReadOnlyList<FirewallRuleOutcome> AllowInboundClients(IReadOnlyList<string> exePaths);
}
