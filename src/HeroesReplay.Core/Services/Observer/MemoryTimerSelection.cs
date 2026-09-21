using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace HeroesReplay.Core.Services.Observer;

public enum MemoryTimerKind
{
    Int32Seconds,
    FloatSeconds,
}

/// <summary>
/// One address whose stored seconds have been compared with the HUD clock.
/// AgreeTicks counts successful ticks after the address was first seen.
/// </summary>
public readonly record struct MemoryTimerCandidate(
    long Address,
    MemoryTimerKind Kind,
    int Seconds,
    int AgreeTicks
);

/// <summary>
/// Pure candidate matching for the read-only match clock. No process access.
/// </summary>
public static class MemoryTimerSelection
{
    public const int MaxMatchSeconds = 4 * 60 * 60;
    public const int MaxCandidates = 48;
    public const int ResumeBelow = 8;
    public const int TicksBeforeLock = 3;
    public const int HudToleranceSeconds = 2;
    public const int MaxHitsPerChunk = 2;
    public const float FloatMatchWindow = 1.2f;
    public const float FloatFractionGap = 0.02f;

    public static bool InRange(int seconds) => seconds >= 0 && seconds <= MaxMatchSeconds;

    public static bool TryMatchStoredClock(
        ReadOnlySpan<byte> bytes,
        MemoryTimerKind kind,
        int hudSeconds,
        out int decodedSeconds
    )
    {
        decodedSeconds = 0;
        if (hudSeconds < 1 || bytes.Length < 4)
        {
            return false;
        }

        if (kind == MemoryTimerKind.Int32Seconds)
        {
            int value = BitConverter.ToInt32(bytes);
            if (value != hudSeconds)
            {
                return false;
            }

            decodedSeconds = value;
            return true;
        }

        float seconds = BitConverter.ToSingle(bytes);
        if (!IsFloatClockNeedle(seconds, hudSeconds))
        {
            return false;
        }

        decodedSeconds = (int)MathF.Floor(seconds);
        return InRange(decodedSeconds);
    }

    /// <summary>
    /// A float clock needle must sit near the HUD second and not on a whole number.
    /// Whole-number floats alias the same small ints that fill the entity heap.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFloatClockNeedle(float seconds, int hudSeconds)
    {
        if (!float.IsFinite(seconds) || hudSeconds < 1)
        {
            return false;
        }

        float delta = seconds - hudSeconds;
        if (delta < -0.05f || delta > FloatMatchWindow)
        {
            return false;
        }

        return MathF.Abs(seconds - MathF.Round(seconds)) >= FloatFractionGap;
    }

    public static bool TryDecode(ReadOnlySpan<byte> bytes, MemoryTimerKind kind, out int seconds)
    {
        seconds = 0;
        if (bytes.Length < 4)
        {
            return false;
        }

        if (kind == MemoryTimerKind.Int32Seconds)
        {
            int value = BitConverter.ToInt32(bytes);
            if (!InRange(value))
            {
                return false;
            }

            seconds = value;
            return true;
        }

        float secondsFloat = BitConverter.ToSingle(bytes);
        if (!float.IsFinite(secondsFloat) || secondsFloat < 0 || secondsFloat > MaxMatchSeconds)
        {
            return false;
        }

        seconds = (int)Math.Floor(secondsFloat);
        return InRange(seconds);
    }

    /// <summary>
    /// True when <paramref name="newValue"/> stayed near the HUD and moved when the HUD moved.
    /// A cell that merely equals an old second is rejected once the HUD advances.
    /// </summary>
    public static bool TracksHud(int previousValue, int newValue, int previousHud, int newHud)
    {
        if (!InRange(newValue) || !InRange(previousHud) || !InRange(newHud))
        {
            return false;
        }

        if (Math.Abs(newValue - newHud) > HudToleranceSeconds)
        {
            return false;
        }

        int expectedDelta = newHud - previousHud;
        int actualDelta = newValue - previousValue;
        if (Math.Abs(actualDelta - expectedDelta) > 1)
        {
            return false;
        }

        if (expectedDelta >= 1 && actualDelta < 1)
        {
            return false;
        }

        if (expectedDelta <= -1 && actualDelta > -1)
        {
            return false;
        }

        return true;
    }

    public static List<MemoryTimerCandidate> KeepTicking(
        IReadOnlyList<MemoryTimerCandidate> previous,
        IReadOnlyDictionary<long, int> readings,
        int previousHud,
        int newHud
    )
    {
        var kept = new List<MemoryTimerCandidate>();
        if (previous == null || previous.Count == 0 || readings == null)
        {
            return kept;
        }

        if (!InRange(previousHud) || !InRange(newHud))
        {
            return kept;
        }

        for (int i = 0; i < previous.Count; i++)
        {
            MemoryTimerCandidate candidate = previous[i];
            if (!readings.TryGetValue(candidate.Address, out int value))
            {
                continue;
            }

            if (!TracksHud(candidate.Seconds, value, previousHud, newHud))
            {
                continue;
            }

            // Only a forward step counts. A cell that stays equal to a paused HUD must not lock.
            int agreeTicks = candidate.AgreeTicks;
            if (value > candidate.Seconds)
            {
                agreeTicks++;
            }

            kept.Add(
                new MemoryTimerCandidate(candidate.Address, candidate.Kind, value, agreeTicks)
            );
        }

        return kept;
    }

    /// <summary>
    /// Lock only after the scan has finished and every remaining candidate has tracked the HUD.
    /// Copies of the same clock may all remain; the lowest int address wins. Overflow refuses.
    /// </summary>
    public static MemoryTimerCandidate? TryLock(
        IReadOnlyList<MemoryTimerCandidate> candidates,
        bool scanFinished,
        bool overflowed
    )
    {
        if (overflowed || !scanFinished || candidates == null || candidates.Count == 0)
        {
            return null;
        }

        int min = int.MaxValue;
        int max = int.MinValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            MemoryTimerCandidate candidate = candidates[i];
            if (candidate.AgreeTicks < TicksBeforeLock)
            {
                return null;
            }

            if (candidate.Seconds < min)
            {
                min = candidate.Seconds;
            }

            if (candidate.Seconds > max)
            {
                max = candidate.Seconds;
            }
        }

        if (max - min > 1)
        {
            return null;
        }

        MemoryTimerCandidate best = candidates[0];
        for (int i = 1; i < candidates.Count; i++)
        {
            MemoryTimerCandidate candidate = candidates[i];
            bool candidateIsInt = candidate.Kind == MemoryTimerKind.Int32Seconds;
            bool bestIsInt = best.Kind == MemoryTimerKind.Int32Seconds;
            if (candidateIsInt && !bestIsInt)
            {
                best = candidate;
            }
            else if (candidateIsInt == bestIsInt && candidate.Address < best.Address)
            {
                best = candidate;
            }
        }

        return best;
    }
}
