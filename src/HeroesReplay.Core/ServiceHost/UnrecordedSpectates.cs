using System;
using System.Collections.Generic;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// The heroesreplay <c>spectate</c> processes that <c>services.json</c> does not list (#381): a
/// <c>spectate file</c> or <c>spectate heroesprofile</c> started by hand. <c>services stop</c> stops
/// the ones that run this install's executable like a recorded role, and never touches one from
/// another install.
/// </summary>
public sealed class UnrecordedSpectates
{
    public const string Role = "spectate";

    public static UnrecordedSpectates None { get; } = new();

    /// <summary>Run this install's executable: asked to exit, then killed after the budget.</summary>
    public IReadOnlyList<ServiceProcessRecord> ThisInstall { get; init; } =
        Array.Empty<ServiceProcessRecord>();

    /// <summary>
    /// Run another executable, or one whose path could not be read. Never stopped, and while one
    /// runs the game and the switchers are left to it.
    /// </summary>
    public IReadOnlyList<ServiceProcessRecord> OtherInstalls { get; init; } =
        Array.Empty<ServiceProcessRecord>();

    /// <summary>
    /// Picks the spectate processes from <paramref name="processes"/>: named heroesreplay, not
    /// <paramref name="selfPid"/>, not in <paramref name="recordedPids"/>, and whose command line
    /// starts with <c>spectate</c>. A process whose command line cannot be read is not counted.
    /// </summary>
    public static UnrecordedSpectates Find(
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
            if (
                arguments.Length == 0
                || !string.Equals(arguments[0], Role, StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            var record = new ServiceProcessRecord
            {
                Name = Role,
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

        return new UnrecordedSpectates { ThisInstall = thisInstall, OtherInstalls = otherInstalls };
    }
}
