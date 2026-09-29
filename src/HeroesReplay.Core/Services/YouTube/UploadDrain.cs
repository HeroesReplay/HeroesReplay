using System;

namespace HeroesReplay.Core.Services.YouTube;

/// <summary>
/// Pending recordings are retried by the uploader that is already running.
/// A failed send does not require a new process.
/// </summary>
public static class UploadDrain
{
    public static readonly TimeSpan Poll = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public static bool ShouldDrain(bool dryRun, DateTimeOffset lastDrain, DateTimeOffset now)
    {
        if (dryRun)
        {
            return false;
        }

        if (now < lastDrain)
        {
            return false;
        }

        return now - lastDrain >= Interval;
    }
}
