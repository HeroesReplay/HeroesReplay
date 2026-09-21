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
        // The first minute of OCR often locks a high glitch (gates offset) then
        // settles on the real 0:xx clock. Allow that correction.
        if (
            candidate < TimeSpan.FromMinutes(3)
            && LastAccepted.Value < TimeSpan.FromMinutes(3)
            && delta > TimeSpan.FromSeconds(-90)
        )
        {
            return true;
        }

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
