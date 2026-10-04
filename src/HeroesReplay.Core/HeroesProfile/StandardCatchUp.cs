using System;
using System.Globalization;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// Keeps the Standard queue near the newest Heroes Profile replays (#205).
/// The stream plays far fewer games a day than Heroes Profile receives, so a cursor that only
/// walks forward falls further behind every day. When the next candidate is older than
/// <see cref="HeroesProfileApiSettings.StandardMaxReplayAge"/>, the cursor jumps to
/// <see cref="HeroesProfileApiSettings.StandardCatchUpWindow"/> ids below the newest id.
/// </summary>
public static class StandardCatchUp
{
    /// <summary>
    /// The cursor to list from instead of <paramref name="candidate"/>, or null to keep it.
    /// A candidate with no game date, a max age of zero, or a target at or below the cursor never jumps.
    /// </summary>
    public static int? JumpTo(
        int cursor,
        HeroesProfileReplay candidate,
        int newestReplayId,
        DateTime nowUtc,
        TimeSpan maxAge,
        int window
    )
    {
        if (candidate == null || maxAge <= TimeSpan.Zero || newestReplayId <= 0)
        {
            return null;
        }

        if (Age(candidate, nowUtc) is not TimeSpan age || age <= maxAge)
        {
            return null;
        }

        int target = newestReplayId - Math.Max(window, 0);
        return target > cursor && target > candidate.Id ? target : null;
    }

    /// <summary>How old the replay's game is. Heroes Profile game dates are UTC.</summary>
    public static TimeSpan? Age(HeroesProfileReplay replay, DateTime nowUtc)
    {
        if (
            string.IsNullOrWhiteSpace(replay?.GameDate)
            || !DateTime.TryParse(
                replay.GameDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime played
            )
        )
        {
            return null;
        }

        return nowUtc - played;
    }
}
