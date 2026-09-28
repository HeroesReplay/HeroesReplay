using System;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// Build 2.55.17.98025 match clock from HeroesOfTheStorm_x64.exe.
/// Ghidra FUN_7ff7438a95e0 returns the live tick accumulator times 1/4096.
/// Read-only. The dynamic page scan is a different thing and stays off.
/// </summary>
public static class MatchTickClock
{
    public const string SupportedBuild = "2.55.17.98025";

    /// <summary>DAT_7ff74516d4a4, the live tick accumulator.</summary>
    public const long MatchTickRva = 0x338D4A4;

    /// <summary>DAT_7ff74442862c, 1/4096. Seconds = ticks * this factor.</summary>
    public const long GameSpeedFactorRva = 0x264862C;

    public static bool IsSupportedVersion(string fileVersion)
    {
        return !string.IsNullOrEmpty(fileVersion)
            && fileVersion.Contains("98025", StringComparison.Ordinal);
    }

    public static bool TrySeconds(int ticks, float speed, out double seconds)
    {
        seconds = 0;
        if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0f)
        {
            return false;
        }

        seconds = ticks * (double)speed;
        if (double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return false;
        }

        // Pre-game is a short negative. A match does not run past 90 minutes.
        return seconds >= -180 && seconds <= 90 * 60;
    }
}
