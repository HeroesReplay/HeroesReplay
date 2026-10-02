using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// Pairs of HUD time and seconds since recording started. The match file is not the HUD clock.
/// </summary>
public sealed class RecordingClock
{
    private readonly object gate = new();
    private readonly List<(double Hud, double File)> samples = new();
    private Stopwatch watch;
    private double lastHud = double.NegativeInfinity;

    public bool IsRunning
    {
        get
        {
            lock (gate)
            {
                return watch != null;
            }
        }
    }

    public int SampleCount
    {
        get
        {
            lock (gate)
            {
                return samples.Count;
            }
        }
    }

    public TimeSpan Elapsed
    {
        get
        {
            lock (gate)
            {
                return watch?.Elapsed ?? TimeSpan.Zero;
            }
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            samples.Clear();
            watch = null;
            lastHud = double.NegativeInfinity;
        }
    }

    public void Start()
    {
        lock (gate)
        {
            samples.Clear();
            watch = Stopwatch.StartNew();
            lastHud = double.NegativeInfinity;
        }
    }

    public void Observe(TimeSpan hud)
    {
        lock (gate)
        {
            if (watch != null)
            {
                ObserveAt(hud, watch.Elapsed.TotalSeconds);
            }
        }
    }

    /// <summary>Records one HUD time against a known recording time.</summary>
    internal void ObserveAt(TimeSpan hud, double fileSeconds)
    {
        lock (gate)
        {
            if (watch == null)
            {
                return;
            }

            double hudSeconds = hud.TotalSeconds;
            if (hudSeconds <= lastHud)
            {
                return;
            }

            lastHud = hudSeconds;
            samples.Add((hudSeconds, fileSeconds));
        }
    }

    public bool TryMap(int hudStart, int hudEnd, out double fileStart, out double duration)
    {
        (double Hud, double File)[] copy;
        lock (gate)
        {
            copy = samples.ToArray();
        }

        return TryMap(copy, hudStart, hudEnd, out fileStart, out duration);
    }

    public static bool TryMap(
        IReadOnlyList<(double Hud, double File)> samples,
        double hudStart,
        double hudEnd,
        out double fileStart,
        out double duration
    )
    {
        fileStart = 0;
        duration = 0;
        if (samples == null || samples.Count < 2 || hudEnd <= hudStart)
        {
            return false;
        }

        if (!TryFileTime(samples, hudStart, out fileStart))
        {
            return false;
        }

        if (!TryFileTime(samples, hudEnd, out double fileEnd) || fileEnd <= fileStart)
        {
            return false;
        }

        duration = fileEnd - fileStart;
        return true;
    }

    private static bool TryFileTime(
        IReadOnlyList<(double Hud, double File)> samples,
        double hud,
        out double file
    )
    {
        file = 0;
        (double Hud, double File) first = samples[0];
        (double Hud, double File) second = samples[1];
        if (second.Hud <= first.Hud)
        {
            return false;
        }

        if (hud <= first.Hud)
        {
            double rate = (second.File - first.File) / (second.Hud - first.Hud);
            file = Math.Max(0, first.File + ((hud - first.Hud) * rate));
            return true;
        }

        for (int i = 1; i < samples.Count; i++)
        {
            (double Hud, double File) left = samples[i - 1];
            (double Hud, double File) right = samples[i];
            if (right.Hud <= left.Hud)
            {
                continue;
            }

            if (hud <= right.Hud)
            {
                double span = (hud - left.Hud) / (right.Hud - left.Hud);
                file = left.File + ((right.File - left.File) * span);
                return true;
            }
        }

        (double Hud, double File) end = samples[^1];
        (double Hud, double File) before = samples[^2];
        if (end.Hud <= before.Hud)
        {
            return false;
        }

        double tail = (end.File - before.File) / (end.Hud - before.Hud);
        file = end.File + ((hud - end.Hud) * tail);
        return file >= 0;
    }
}
