using System;
using System.Globalization;

namespace HeroesReplay.Core.Retention;

/// <summary>
/// <c>check storage</c> (#487): the disk now, and the last housekeeping pass. Never a failure: a
/// pass that never ran, ran long ago, or failed is a warning.
/// </summary>
public sealed record HousekeepingCheckReport(bool Warning, string Reason, string Detail);

public static class HousekeepingCheck
{
    public const string NeverRan = "never_ran";
    public const string Stale = "stale";
    public const string LastFailed = "last_failed";

    /// <summary>A pass older than this many intervals means no supervisor runs them.</summary>
    public const int StaleAfterIntervals = 3;

    public static HousekeepingCheckReport Evaluate(
        StorageSummary disk,
        HousekeepingState state,
        HousekeepingSettings settings,
        DateTimeOffset utcNow
    )
    {
        settings ??= new HousekeepingSettings();
        string storage = Describe(disk);
        HousekeepingRun last = state?.LastRun;
        if (last == null)
        {
            return new(
                true,
                NeverRan,
                storage
                    + " No housekeeping pass is recorded (housekeeping.json). "
                    + (
                        settings.Enabled
                            ? "The supervisor runs one when it starts and every Housekeeping:Interval; `heroesreplay storage report --apply` runs one now."
                            : "Housekeeping:Enabled is false, so the supervisor runs none."
                    )
            );
        }

        string lastLine =
            $" Last housekeeping pass {last.StartedAt.ToString("u", CultureInfo.InvariantCulture)} ({last.Trigger}): {last.Describe()}.";
        if (last.Outcome == HousekeepingRun.Failed)
        {
            return new(
                true,
                LastFailed,
                storage + lastLine + " Its log names the pruner that failed."
            );
        }

        TimeSpan limit = TimeSpan.FromTicks(settings.EffectiveInterval.Ticks * StaleAfterIntervals);
        if (settings.Enabled && utcNow - last.StartedAt > limit)
        {
            return new(
                true,
                Stale,
                storage
                    + lastLine
                    + $" That is more than {StaleAfterIntervals} Housekeeping:Interval ago: is the supervisor running (`heroesreplay services status`)?"
            );
        }

        return new(false, null, storage + lastLine);
    }

    public static string Describe(StorageSummary disk)
    {
        if (disk == null)
        {
            return "The disk was not measured.";
        }

        string largest =
            disk.LargestCategory == null
                ? string.Empty
                : $", largest {disk.LargestCategory} {Size.Describe(disk.LargestBytes)}";
        string free = disk.FreeBytes is long bytes
            ? Size.Describe(bytes) + " free"
            : "free space unknown";
        return $"Data {Size.Describe(disk.DataBytes)}{largest}; per-user state {Size.Describe(disk.StateBytes)}; {free}.";
    }
}
