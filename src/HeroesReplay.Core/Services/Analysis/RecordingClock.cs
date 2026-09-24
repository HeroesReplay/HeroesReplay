using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.Analysis;

/// <summary>
/// Pairs of match-clock time and seconds into the OBS file. The file keeps
/// running while the replay pauses or skips, so HUD time is not file time.
/// </summary>
public sealed class RecordingClock
{
    private readonly List<Sample> samples = new();

    public void Observe(TimeSpan hud, TimeSpan file)
    {
        if (hud < TimeSpan.Zero || file < TimeSpan.Zero)
        {
            return;
        }

        samples.Add(new Sample((int)Math.Floor(hud.TotalSeconds), file.TotalSeconds));
    }

    public bool TryFileSeconds(int hudSecond, out double fileSeconds)
    {
        fileSeconds = 0;
        bool found = false;
        foreach (Sample sample in samples)
        {
            if (sample.HudSecond < hudSecond)
            {
                continue;
            }

            if (!found || sample.FileSeconds < fileSeconds)
            {
                fileSeconds = sample.FileSeconds;
                found = true;
            }
        }

        return found;
    }

    private readonly record struct Sample(int HudSecond, double FileSeconds);
}
