using System;
using System.Collections.Generic;
using System.IO;

namespace HeroesReplay.Core.Retention;

/// <summary>
/// The supervisor's housekeeping pass (#487): every registered pruner, on a timer and when the
/// supervisor starts, so cleanup no longer waits for a role to be busy. Each policy lives in its
/// own section (<c>Retention</c>, and the pruners added under #496); this section only says when
/// the pass runs, which pruners it runs, and whether it removes anything.
/// </summary>
public sealed class HousekeepingSettings
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

    /// <summary>False: the supervisor runs no pass. <c>storage report</c> still plans.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Time between passes. The first runs when the supervisor starts.</summary>
    public TimeSpan Interval { get; set; } = DefaultInterval;

    /// <summary>
    /// True: every pass, and every in-role retention sweep, logs what it would remove and removes
    /// nothing.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Each pruner's switch by name (<c>"media": false</c> turns that one off). A pruner that is
    /// not listed runs.
    /// </summary>
    public Dictionary<string, bool> Pruners { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <c>%LOCALAPPDATA%\HeroesReplay</c> when empty: where <c>housekeeping.json</c> is written,
    /// and the root of the per-user storage the report measures.
    /// </summary>
    public string StateDirectory { get; set; } = string.Empty;

    public TimeSpan EffectiveInterval => Interval > TimeSpan.Zero ? Interval : DefaultInterval;

    public bool IsEnabled(string pruner) =>
        string.IsNullOrWhiteSpace(pruner)
        || Pruners == null
        || !Pruners.TryGetValue(pruner, out bool enabled)
        || enabled;

    public static string DefaultStateDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay"
        );

    public string ResolvedStateDirectory =>
        string.IsNullOrWhiteSpace(StateDirectory)
            ? DefaultStateDirectory
            : Environment.ExpandEnvironmentVariables(StateDirectory);
}
