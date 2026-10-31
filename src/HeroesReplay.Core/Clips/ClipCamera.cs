using System;
using System.Collections.Generic;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Clips;

/// <summary>
/// How much of a clip's moment the camera showed. <see cref="Seconds"/> runs from the clip's first
/// blow (or the start of its boss fight) to its last blow (or the capture); <see cref="Shown"/> of
/// them had the camera on the clip. <see cref="Moment"/> is whether it was there for the end of it.
/// </summary>
public readonly record struct ClipCoverage(int Shown, int Seconds, bool Moment)
{
    public double Share => Seconds == 0 ? 0 : Shown / (double)Seconds;
}

/// <summary>
/// A clip is cut from the recording, and the recording shows what the camera showed, while the
/// clip analyzers find moments in the replay wherever they happened. A clip is only worth cutting
/// when the camera was there: the focus timeline the spectator followed (requests and hero focus
/// included) had a hero within <see cref="ClipSettings.CameraRadius"/> of the clip's place (a boss
/// pit) or of the player whose kills it shows, for at least <see cref="ClipSettings.CameraShare"/>
/// of the moment and in its last <see cref="MomentSeconds"/> seconds. Without a timeline nothing is
/// known, and every clip is kept.
/// </summary>
public static class ClipCamera
{
    /// <summary>The seconds up to and including the last blow or the capture that the camera must catch one of.</summary>
    public const int MomentSeconds = 3;

    public static bool Shows(
        TeamKillClip clip,
        IReadOnlyDictionary<TimeSpan, Focus> camera,
        IHeroPositions positions,
        ClipSettings settings
    )
    {
        if (camera == null || camera.Count == 0)
        {
            return true;
        }

        ClipCoverage coverage = Coverage(clip, camera, positions, settings);
        return coverage.Moment && coverage.Share >= (settings ?? new ClipSettings()).CameraShare;
    }

    public static ClipCoverage Coverage(
        TeamKillClip clip,
        IReadOnlyDictionary<TimeSpan, Focus> camera,
        IHeroPositions positions,
        ClipSettings settings
    )
    {
        double radius = (settings ?? new ClipSettings()).CameraRadius;
        int first = Math.Min(clip.FirstDeathSecond, clip.LastDeathSecond);
        int last = clip.LastDeathSecond;
        int shown = 0;
        bool moment = false;
        for (int second = first; second <= last; second++)
        {
            if (!At(clip, camera, positions, radius, second))
            {
                continue;
            }

            shown++;
            if (last - second < MomentSeconds)
            {
                moment = true;
            }
        }

        return new ClipCoverage(shown, last - first + 1, moment);
    }

    private static bool At(
        TeamKillClip clip,
        IReadOnlyDictionary<TimeSpan, Focus> camera,
        IHeroPositions positions,
        double radius,
        int second
    )
    {
        if (
            camera == null
            || !camera.TryGetValue(TimeSpan.FromSeconds(second), out Focus focus)
            || focus == null
        )
        {
            return false;
        }

        if (clip.Player >= 0 && focus.Index == clip.Player)
        {
            return true;
        }

        if (positions == null)
        {
            return false;
        }

        TimeSpan time = TimeSpan.FromSeconds(second);
        MapPoint place;
        if (clip.Point is MapPoint point)
        {
            place = point;
        }
        else if (clip.Player < 0 || !positions.TryGet(clip.Player, time, out place))
        {
            return false;
        }

        return positions.TryGet(focus.Index, time, out MapPoint shown)
            && shown.DistanceTo(place) <= radius;
    }
}
