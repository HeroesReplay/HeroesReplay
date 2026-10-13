using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// The heroesreplay role processes that <c>services.json</c> does not list: a <c>spectate file</c>
/// or <c>spectate heroesprofile</c> started by hand (#381), and any role that runs untracked next
/// to a <c>services.json</c> that names a dead pid, such as the spectate a lost restart left on
/// the stream PC on 2026-10-09 (#397). <c>services stop</c> stops the ones that run this
/// install's executable like a recorded role, and never touches one from another install.
/// </summary>
public sealed class UnrecordedRoles
{
    public const string Role = "spectate";

    public static UnrecordedRoles None { get; } = new();

    /// <summary>Run this install's executable: asked to exit, then killed after the budget.</summary>
    public IReadOnlyList<ServiceProcessRecord> ThisInstall { get; init; } =
        Array.Empty<ServiceProcessRecord>();

    /// <summary>
    /// Run another executable, or one whose path could not be read. Never stopped. While a
    /// spectate of these runs, the game and the switchers are left to it.
    /// </summary>
    public IReadOnlyList<ServiceProcessRecord> OtherInstalls { get; init; } =
        Array.Empty<ServiceProcessRecord>();

    /// <summary>A spectate of another install runs: the game is its, not this stop's.</summary>
    public bool OtherInstallSpectates =>
        OtherInstalls.Any(record =>
            string.Equals(record.Name, Role, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>
    /// Picks the role processes from <paramref name="processes"/>: named heroesreplay, not
    /// <paramref name="selfPid"/>, not in <paramref name="recordedPids"/>, and whose command line
    /// starts with <c>spectate</c> or is exactly another role's command (<c>twitch connect</c>,
    /// <c>heroesprofile download</c>, <c>youtube uploader</c>). A process whose command line cannot
    /// be read is not counted.
    /// </summary>
    public static UnrecordedRoles Find(
        IEnumerable<ProcessTableEntry> processes,
        Func<int, string> commandLineOrNull,
        string installExecutable,
        int selfPid,
        IReadOnlyCollection<int> recordedPids
    )
    {
        ArgumentNullException.ThrowIfNull(commandLineOrNull);
        var recorded = new HashSet<int>(recordedPids ?? Array.Empty<int>());
        var thisInstall = new List<ServiceProcessRecord>();
        var otherInstalls = new List<ServiceProcessRecord>();
        foreach (ProcessTableEntry entry in processes ?? Array.Empty<ProcessTableEntry>())
        {
            if (
                entry == null
                || entry.Pid <= 0
                || entry.Pid == selfPid
                || recorded.Contains(entry.Pid)
                || !ServiceProcessPlan.IsHeroesReplay(entry.Name)
            )
            {
                continue;
            }

            string[] arguments = ProcessCommandLine.Arguments(commandLineOrNull(entry.Pid));
            string role = RoleOf(arguments);
            if (role == null)
            {
                continue;
            }

            var record = new ServiceProcessRecord
            {
                Name = role,
                Pid = entry.Pid,
                Arguments = string.Join(' ', arguments),
                ExecutablePath = entry.ImagePath,
                StartedAt = entry.StartTime,
            };
            bool ours =
                !string.IsNullOrWhiteSpace(entry.ImagePath)
                && !string.IsNullOrWhiteSpace(installExecutable)
                && ServiceProcessPlan.SamePath(entry.ImagePath, installExecutable);
            (ours ? thisInstall : otherInstalls).Add(record);
        }

        return new UnrecordedRoles { ThisInstall = thisInstall, OtherInstalls = otherInstalls };
    }

    /// <summary>
    /// <c>spectate</c> for any spectate command; the role whose command the arguments are
    /// exactly, for the others; null for every other heroesreplay command.
    /// </summary>
    private static string RoleOf(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return null;
        }

        if (string.Equals(arguments[0], Role, StringComparison.OrdinalIgnoreCase))
        {
            return Role;
        }

        foreach ((string name, string command) in ServiceProcessPlan.All)
        {
            if (
                arguments.SequenceEqual(
                    command.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                return name;
            }
        }

        return null;
    }
}
