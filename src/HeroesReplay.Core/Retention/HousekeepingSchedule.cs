using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Retention;

/// <summary>
/// When the supervisor runs the housekeeping pass (#487): on its first tick, then once per
/// <see cref="HousekeepingSettings.Interval"/>. The pass runs off the supervisor's thread, so a
/// slow disk never delays a role restart, and a pass still running when the next one is due is
/// not started twice.
/// </summary>
public sealed class HousekeepingSchedule
{
    private readonly HousekeepingSettings settings;
    private readonly Action<DateTimeOffset> run;
    private readonly Action<Action> start;
    private readonly ILogger logger;
    private DateTimeOffset? lastStarted;
    private int running;

    /// <summary>
    /// <paramref name="run"/> is one pass, given the time it was due. <paramref name="start"/>
    /// starts it; null runs it on the thread pool.
    /// </summary>
    public HousekeepingSchedule(
        HousekeepingSettings settings,
        Action<DateTimeOffset> run,
        ILogger logger,
        Action<Action> start = null
    )
    {
        this.settings = settings ?? new HousekeepingSettings();
        this.run = run ?? throw new ArgumentNullException(nameof(run));
        this.logger = logger;
        this.start = start ?? (work => Task.Run(work));
    }

    public bool Running => Volatile.Read(ref running) == 1;

    public DateTimeOffset? LastStarted => lastStarted;

    /// <summary>Starts a pass when one is due and none is running. True when it started one.</summary>
    public bool Tick(DateTimeOffset now)
    {
        if (!settings.Enabled)
        {
            return false;
        }

        if (lastStarted is DateTimeOffset last && now - last < settings.EffectiveInterval)
        {
            return false;
        }

        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            return false;
        }

        lastStarted = now;
        try
        {
            start(() => RunOnce(now));
        }
        catch (Exception e)
        {
            Volatile.Write(ref running, 0);
            logger?.LogWarning(e, "Could not start the housekeeping pass.");
        }

        return true;
    }

    private void RunOnce(DateTimeOffset now)
    {
        try
        {
            run(now);
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "The housekeeping pass failed.");
        }
        finally
        {
            Volatile.Write(ref running, 0);
        }
    }
}
