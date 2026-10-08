using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// One bounded, read-only check of the live dependency a role needs (#305): the Heroes Profile
/// API for download, YouTube OAuth for youtube, the OBS websocket for spectate, the Twitch token
/// for twitch. A failed check makes the role degraded with a stable cause code, never failed, so
/// the supervisor does not restart it and an outage or a bad key cannot cause a restart loop.
/// </summary>
public interface IServiceDependencyProbe
{
    /// <summary>What is checked, for causes a person reads: "Heroes Profile API".</summary>
    string Dependency { get; }

    /// <summary>
    /// Null when the role uses the dependency in this configuration. Otherwise why not (a dry
    /// run, OBS off): the probe is then never sent.
    /// </summary>
    string NotUsedReason { get; }

    /// <summary>
    /// The result when the check did not finish inside its bound or threw something the probe
    /// did not classify: the dependency is unreachable.
    /// </summary>
    ServiceDependencyResult Unreachable(string cause);

    /// <summary>
    /// One check. It must not spend quota, change anything, or put a token in a cause. The token
    /// is cancelled when the bound (<c>ServiceHealth:DependencyProbeTimeout</c>) or the role's stop
    /// is reached.
    /// </summary>
    Task<ServiceDependencyResult> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>What a probe found. <see cref="ServiceRoleDependency.State"/> uses these.</summary>
public static class ServiceDependencyStates
{
    /// <summary>The dependency answered and accepted this install's credentials.</summary>
    public const string Ok = "ok";

    /// <summary>The dependency answered and refused: a bad key, a revoked token, a wrong password.</summary>
    public const string Rejected = "rejected";

    /// <summary>The dependency did not answer: an outage, a timeout, a server error.</summary>
    public const string Unreachable = "unreachable";

    /// <summary>Not checked this time and nothing is wrong (OBS is closed between replays).</summary>
    public const string Skipped = "skipped";

    /// <summary>This role does not use the dependency in this configuration. Never checked.</summary>
    public const string Unused = "unused";

    /// <summary>Rejected or unreachable: the role is degraded with the result's code.</summary>
    public static bool IsFailure(string state) =>
        string.Equals(state, Rejected, StringComparison.Ordinal)
        || string.Equals(state, Unreachable, StringComparison.Ordinal);
}

/// <summary>One probe's verdict, with a stable <see cref="Code"/> and the fix when it failed.</summary>
public sealed record ServiceDependencyResult(
    string Dependency,
    string State,
    string Code,
    string Cause,
    string Remediation
)
{
    public bool Failed => ServiceDependencyStates.IsFailure(State);

    public static ServiceDependencyResult Ok(string dependency, string cause) =>
        new(dependency, ServiceDependencyStates.Ok, null, cause, null);

    public static ServiceDependencyResult Skipped(string dependency, string cause) =>
        new(dependency, ServiceDependencyStates.Skipped, null, cause, null);

    public static ServiceDependencyResult Unused(string dependency, string cause) =>
        new(dependency, ServiceDependencyStates.Unused, null, cause, null);

    public static ServiceDependencyResult Rejected(
        string dependency,
        string code,
        string cause,
        string remediation
    ) => new(dependency, ServiceDependencyStates.Rejected, code, cause, remediation);

    public static ServiceDependencyResult Unreachable(
        string dependency,
        string code,
        string cause,
        string remediation
    ) => new(dependency, ServiceDependencyStates.Unreachable, code, cause, remediation);
}
