using System;
using System.Collections.Generic;
using Heroes.ReplayParser;

namespace HeroesReplay.Core.Analysis.Insights.Snapshot;

/// <summary>Where a player's hero was at a moment of the match.</summary>
public interface IHeroPositions
{
    /// <summary>
    /// The player's last sampled position at <paramref name="time"/> while one of their hero units
    /// is alive, or false when the hero is dead or the last sample is too old.
    /// </summary>
    bool TryGet(int playerIndex, TimeSpan time, out MapPoint point);
}

/// <summary>
/// Hero positions from the replay (#412). The parser samples a hero about once a second
/// (median gap 1 s, p90 2 s on build 98304, most of them estimated between tracker samples).
/// A sample older than <c>maxAge</c> is not used, so a hero is never placed where it was long ago.
/// </summary>
public sealed class HeroPositions : IHeroPositions
{
    private readonly Dictionary<int, Track> tracks = new();
    private readonly TimeSpan maxAge;

    public HeroPositions(IReadOnlyList<Player> players, TimeSpan maxAge)
    {
        this.maxAge = maxAge;
        for (int index = 0; index < (players?.Count ?? 0); index++)
        {
            Player player = players[index];
            if (player?.HeroUnits == null)
            {
                continue;
            }

            var samples = new List<(TimeSpan Time, MapPoint Point)>();
            var lives = new List<(TimeSpan Born, TimeSpan Died)>();
            foreach (Unit unit in player.HeroUnits)
            {
                if (unit == null)
                {
                    continue;
                }

                lives.Add((unit.TimeSpanBorn, unit.TimeSpanDied ?? TimeSpan.MaxValue));
                foreach (Position position in unit.Positions ?? new List<Position>())
                {
                    if (position?.Point != null)
                    {
                        samples.Add(
                            (position.TimeSpan, new MapPoint(position.Point.X, position.Point.Y))
                        );
                    }
                }
            }

            samples.Sort((left, right) => left.Time.CompareTo(right.Time));
            tracks[index] = new Track(samples, lives);
        }
    }

    public bool TryGet(int playerIndex, TimeSpan time, out MapPoint point)
    {
        point = default;
        if (!tracks.TryGetValue(playerIndex, out Track track) || !track.IsAlive(time))
        {
            return false;
        }

        int found = track.LastAtOrBefore(time);
        if (found < 0 || time - track.Samples[found].Time > maxAge)
        {
            return false;
        }

        point = track.Samples[found].Point;
        return true;
    }

    private sealed record Track(
        List<(TimeSpan Time, MapPoint Point)> Samples,
        List<(TimeSpan Born, TimeSpan Died)> Lives
    )
    {
        public bool IsAlive(TimeSpan time)
        {
            foreach ((TimeSpan born, TimeSpan died) in Lives)
            {
                if (time >= born && time < died)
                {
                    return true;
                }
            }

            return false;
        }

        public int LastAtOrBefore(TimeSpan time)
        {
            int low = 0;
            int high = Samples.Count - 1;
            int found = -1;
            while (low <= high)
            {
                int middle = (low + high) / 2;
                if (Samples[middle].Time <= time)
                {
                    found = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return found;
        }
    }
}
