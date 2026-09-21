using System;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// Accepts OCR match clocks that move forward like a real timer.
/// Large jumps or rewinds are treated as parse glitches.
/// </summary>
public sealed class MatchTimerFilter
{
    public TimeSpan? LastAccepted { get; private set; }

    public void Reset() => LastAccepted = null;

    public bool IsPlausible(TimeSpan candidate, TimeSpan maxJump)
    {
        if (LastAccepted == null)
        {
            return true;
        }

        TimeSpan delta = candidate - LastAccepted.Value;
        if (delta < TimeSpan.FromSeconds(-3))
        {
            return false;
        }

        if (delta > maxJump)
        {
            return false;
        }

        return true;
    }

    public void Accept(TimeSpan candidate) => LastAccepted = candidate;
}
