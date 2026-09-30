using System;

namespace HeroesReplay.Core.Services.Connectivity;

public enum OperatingMode
{
    Online,
    ShortOutage,
    ExtendedOutage,
    Recovering,
}

/// <summary>
/// One operating mode for a multi-hour outage. Extended mode pauses new downloads.
/// The downloader process stays up and probes again after <see cref="PauseDelay"/>.
/// </summary>
public static class OutageMode
{
    public static readonly TimeSpan ExtendedAfter = TimeSpan.FromHours(1);
    public static readonly TimeSpan RecoverStableFor = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan PauseDelay = TimeSpan.FromMinutes(5);

    public static OperatingMode Decide(bool internetUp, TimeSpan downFor, TimeSpan stableFor)
    {
        if (downFor < TimeSpan.Zero)
        {
            downFor = TimeSpan.Zero;
        }

        if (stableFor < TimeSpan.Zero)
        {
            stableFor = TimeSpan.Zero;
        }

        if (!internetUp)
        {
            return downFor >= ExtendedAfter
                ? OperatingMode.ExtendedOutage
                : OperatingMode.ShortOutage;
        }

        if (downFor == TimeSpan.Zero && stableFor == TimeSpan.Zero)
        {
            return OperatingMode.Online;
        }

        if (stableFor >= RecoverStableFor)
        {
            return OperatingMode.Online;
        }

        return OperatingMode.Recovering;
    }

    public static bool MayDownload(OperatingMode mode)
    {
        return mode != OperatingMode.ExtendedOutage;
    }

    public static bool MaySpectate(OperatingMode mode)
    {
        return mode != OperatingMode.ExtendedOutage;
    }
}
