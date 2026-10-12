using System;

namespace HeroesReplay.Core.Analysis.Insights.Snapshot;

/// <summary>A point on the map in replay map units (maps are about 250 wide).</summary>
public readonly record struct MapPoint(int X, int Y)
{
    public double DistanceTo(MapPoint other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
