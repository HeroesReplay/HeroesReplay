using System;
using System.Collections.Generic;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Clips;

/// <summary>
/// How much of a clip's moment the camera showed. <see cref="Seconds"/> runs from the clip's first
/// blow (or the start of its boss fight) to its last blow (or the capture); <see cref="Shown"/> of
/// them had the camera on the clip. <see cref="Moment"/> is whether it was there for the end of it.
/// For a kill clip, <see cref="BlowsShown"/> of its <see cref="Blows"/> killing blows had the killer
/// and the victim on screen (<see cref="ClipCamera.ShowsBlow"/>).
/// </summary>
public readonly record struct ClipCoverage(
    int Shown,
    int Seconds,
    bool Moment,
    int BlowsShown = 0,
    int Blows = 0
)
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
/// <para>
/// A kill clip (a triple kill, quadra kill or pentakill) also needs its blows on screen. Near the
/// killer is not enough: a hero focus replay locks the camera on one hero, and a quadra kill by a
/// hero at the edge of that screen, with its victims off it, shows little (replay 66698689, Garden
/// of Terror: Qhira's quadra kill under Hanzo's camera). A blow is on screen when the camera
/// follows the killer, or when the killer and the victim (where it last stood) are both inside the
/// screen around the followed hero (<see cref="ClipSettings.CameraViewHalfWidth"/>,
/// <see cref="ClipSettings.CameraViewUp"/>, <see cref="ClipSettings.CameraViewDown"/>). At least
/// <see cref="ClipSettings.CameraBlowShare"/> of the blows must be.
/// </para>
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

        settings ??= new ClipSettings();
        ClipCoverage coverage = Coverage(clip, camera, positions, settings);
        return coverage.Moment
            && coverage.Share >= settings.CameraShare
            && (
                coverage.Blows == 0
                || coverage.BlowsShown >= BlowsNeeded(coverage.Blows, settings.CameraBlowShare)
            );
    }

    /// <summary>The blows of <paramref name="blows"/> that must be on screen: the share, rounded up.</summary>
    public static int BlowsNeeded(int blows, double share) =>
        Math.Min(blows, (int)Math.Ceiling(blows * Math.Clamp(share, 0, 1) - 1e-9));

    public static ClipCoverage Coverage(
        TeamKillClip clip,
        IReadOnlyDictionary<TimeSpan, Focus> camera,
        IHeroPositions positions,
        ClipSettings settings
    )
    {
        settings ??= new ClipSettings();
        int first = Math.Min(clip.FirstDeathSecond, clip.LastDeathSecond);
        int last = clip.LastDeathSecond;
        int shown = 0;
        bool moment = false;
        for (int second = first; second <= last; second++)
        {
            if (!At(clip, camera, positions, settings, second))
            {
                continue;
            }

            shown++;
            if (last - second < MomentSeconds)
            {
                moment = true;
            }
        }

        int blows = 0;
        int blowsShown = 0;
        if (clip.Point == null && clip.Player >= 0 && clip.Kills != null)
        {
            foreach (TeamKillBlow blow in clip.Kills)
            {
                blows++;
                if (ShowsBlow(clip.Player, blow, camera, positions, settings))
                {
                    blowsShown++;
                }
            }
        }

        return new ClipCoverage(shown, last - first + 1, moment, blowsShown, blows);
    }

    /// <summary>
    /// Whether the camera showed one killing blow: it followed the killer at that second, or the
    /// killer and the victim were both on the screen around the hero it followed. The victim is
    /// placed where it stood a second before it died; a victim that cannot be placed is judged by
    /// the killer alone.
    /// </summary>
    public static bool ShowsBlow(
        int killer,
        TeamKillBlow blow,
        IReadOnlyDictionary<TimeSpan, Focus> camera,
        IHeroPositions positions,
        ClipSettings settings
    )
    {
        TimeSpan time = TimeSpan.FromSeconds(blow.Second);
        if (camera == null || !camera.TryGetValue(time, out Focus focus) || focus == null)
        {
            return false;
        }

        if (focus.Index == killer)
        {
            return true;
        }

        if (
            positions == null
            || !positions.TryGet(focus.Index, time, out MapPoint center)
            || !positions.TryGet(killer, time, out MapPoint killerPoint)
            || !OnScreen(center, killerPoint, settings)
        )
        {
            return false;
        }

        return blow.VictimKey < 0
            || !Victim(positions, blow.VictimKey, time, out MapPoint victim)
            || OnScreen(center, victim, settings);
    }

    /// <summary>
    /// Whether <paramref name="point"/> is inside the screen the camera shows around the hero it
    /// follows at <paramref name="center"/>. The camera is tilted, so it shows more of the map above
    /// the hero than below it.
    /// </summary>
    public static bool OnScreen(MapPoint center, MapPoint point, ClipSettings settings)
    {
        settings ??= new ClipSettings();
        int dx = point.X - center.X;
        int dy = point.Y - center.Y;
        return Math.Abs(dx) <= settings.CameraViewHalfWidth
            && dy <= settings.CameraViewUp
            && -dy <= settings.CameraViewDown;
    }

    private static bool Victim(
        IHeroPositions positions,
        int victim,
        TimeSpan death,
        out MapPoint point
    )
    {
        for (int before = 1; before <= 2; before++)
        {
            if (positions.TryGet(victim, death - TimeSpan.FromSeconds(before), out point))
            {
                return true;
            }
        }

        return positions.TryGet(victim, death, out point);
    }

    private static bool At(
        TeamKillClip clip,
        IReadOnlyDictionary<TimeSpan, Focus> camera,
        IHeroPositions positions,
        ClipSettings settings,
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

        if (!positions.TryGet(focus.Index, time, out MapPoint shown))
        {
            return false;
        }

        return clip.OnScreen
            ? OnScreen(shown, place, settings)
            : shown.DistanceTo(place) <= settings.CameraRadius;
    }
}
