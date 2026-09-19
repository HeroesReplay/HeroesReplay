using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Analysis.Calculators;

namespace HeroesReplay.Core.Services.Analysis;

public sealed class ReplayTimeline
{
    private readonly Focus[] slots;
    private readonly List<Unit>[] aliveHeroes;
    private readonly Dictionary<Unit, Point[]> pointsByUnit;
    private readonly Dictionary<Unit, bool[]> hasPointByUnit;
    private readonly List<TrackerEvent>[] trackerBySecond;
    private readonly List<GameEvent>[] gameEventsBySecond;

    public Replay Replay { get; }
    public int TotalSeconds { get; }
    public IReadOnlyList<Unit> HeroUnits { get; private set; }

    private ReplayTimeline(Replay replay, int totalSeconds)
    {
        Replay = replay;
        TotalSeconds = totalSeconds;
        slots = new Focus[totalSeconds];
        aliveHeroes = new List<Unit>[totalSeconds];
        pointsByUnit = new Dictionary<Unit, Point[]>();
        hasPointByUnit = new Dictionary<Unit, bool[]>();
        trackerBySecond = new List<TrackerEvent>[totalSeconds];
        gameEventsBySecond = new List<GameEvent>[totalSeconds];

        for (int i = 0; i < totalSeconds; i++)
        {
            aliveHeroes[i] = new List<Unit>(10);
        }
    }

    public static ReplayTimeline Create(Replay replay)
    {
        if (replay == null)
        {
            throw new ArgumentNullException(nameof(replay));
        }

        int totalSeconds = Math.Max(1, (int)Math.Ceiling(replay.ReplayLength.TotalSeconds));
        var timeline = new ReplayTimeline(replay, totalSeconds);

        var heroes = new List<Unit>();
        foreach (Player player in replay.Players ?? Array.Empty<Player>())
        {
            foreach (Unit unit in player.HeroUnits ?? new List<Unit>())
            {
                heroes.Add(unit);
                timeline.IndexHero(unit);
            }
        }

        timeline.HeroUnits = heroes;
        timeline.IndexTrackerEvents();
        timeline.IndexGameEvents();
        return timeline;
    }

    public IReadOnlyList<Unit> AliveHeroesAt(int second)
    {
        if ((uint)second >= (uint)TotalSeconds)
        {
            return Array.Empty<Unit>();
        }

        return aliveHeroes[second];
    }

    public bool TryGetPoint(Unit unit, int second, out Point point)
    {
        point = default;
        if (unit == null || (uint)second >= (uint)TotalSeconds)
        {
            return false;
        }

        if (!hasPointByUnit.TryGetValue(unit, out bool[] has) || !has[second])
        {
            return false;
        }

        point = pointsByUnit[unit][second];
        return true;
    }

    public IReadOnlyList<TrackerEvent> TrackerEventsAt(int second)
    {
        if ((uint)second >= (uint)TotalSeconds || trackerBySecond[second] == null)
        {
            return Array.Empty<TrackerEvent>();
        }

        return trackerBySecond[second];
    }

    public IReadOnlyList<GameEvent> GameEventsAt(int second)
    {
        if ((uint)second >= (uint)TotalSeconds || gameEventsBySecond[second] == null)
        {
            return Array.Empty<GameEvent>();
        }

        return gameEventsBySecond[second];
    }

    public void Offer(
        TimeSpan time,
        Type calculator,
        Unit unit,
        Player target,
        float points,
        string description
    )
    {
        int second = time.FloorSeconds();
        if ((uint)second >= (uint)TotalSeconds || unit == null || target == null)
        {
            return;
        }

        Focus existing = slots[second];
        if (existing == null || existing.Points < points)
        {
            slots[second] = new Focus(calculator, unit, target, points, description);
        }
    }

    public void ApplyDeathContext(SpectateSettings spectate)
    {
        if (spectate == null)
        {
            return;
        }

        int past = Math.Max(0, (int)spectate.PastDeathContextTime.TotalSeconds);
        int present = Math.Max(0, (int)spectate.PresentDeathContextTime.TotalSeconds);
        var processed = new HashSet<Unit>();

        for (int second = 0; second < TotalSeconds; second++)
        {
            Focus focus = slots[second];
            if (focus == null)
            {
                continue;
            }

            if (
                focus.Calculator != typeof(KillCalculator)
                && focus.Calculator != typeof(DeathCalculator)
            )
            {
                continue;
            }

            if (!processed.Add(focus.Unit))
            {
                continue;
            }

            for (int delta = 1; delta < past; delta++)
            {
                int t = second - delta;
                if (t < 0)
                {
                    break;
                }

                if (slots[t] == null || slots[t].Points < focus.Points)
                {
                    slots[t] = focus;
                }
            }

            for (int delta = 1; delta < present; delta++)
            {
                int t = second + delta;
                if (t >= TotalSeconds)
                {
                    break;
                }

                Focus future = slots[t];
                if (future != null && future.Unit.IsAliveAt(TimeSpan.FromSeconds(t)))
                {
                    if (focus.Points > future.Points)
                    {
                        slots[t] = focus;
                    }
                }
                else
                {
                    slots[t] = focus;
                }
            }
        }
    }

    public IReadOnlyDictionary<TimeSpan, Focus> ToDictionary()
    {
        Player[] players = Replay.Players ?? Array.Empty<Player>();
        var map = new SortedDictionary<TimeSpan, Focus>();

        for (int second = 0; second < TotalSeconds; second++)
        {
            Focus focus = slots[second];
            if (focus == null)
            {
                continue;
            }

            int index = Array.IndexOf(players, focus.Target);
            map[TimeSpan.FromSeconds(second)] = new Focus(
                focus.Calculator,
                focus.Unit,
                focus.Target,
                focus.Points,
                focus.Description,
                index
            );
        }

        return new ReadOnlyDictionary<TimeSpan, Focus>(map);
    }

    private void IndexHero(Unit unit)
    {
        var points = new Point[TotalSeconds];
        var hasPoint = new bool[TotalSeconds];

        if (unit.Positions != null)
        {
            foreach (Position position in unit.Positions)
            {
                int second = position.TimeSpan.FloorSeconds();
                if ((uint)second >= (uint)TotalSeconds)
                {
                    continue;
                }

                points[second] = position.Point;
                hasPoint[second] = true;
            }
        }

        int born = unit.TimeSpanBorn.FloorSeconds();
        int died = unit.TimeSpanDied.HasValue
            ? unit.TimeSpanDied.Value.FloorSeconds()
            : TotalSeconds;
        Point last = default;
        bool has = false;

        for (int second = born; second < died && second < TotalSeconds; second++)
        {
            if (hasPoint[second])
            {
                last = points[second];
                has = true;
            }
            else if (has)
            {
                points[second] = last;
                hasPoint[second] = true;
            }

            aliveHeroes[second].Add(unit);
        }

        pointsByUnit[unit] = points;
        hasPointByUnit[unit] = hasPoint;
    }

    private void IndexTrackerEvents()
    {
        if (Replay.TrackerEvents == null)
        {
            return;
        }

        foreach (TrackerEvent trackerEvent in Replay.TrackerEvents)
        {
            int second = trackerEvent.TimeSpan.FloorSeconds();
            if ((uint)second >= (uint)TotalSeconds)
            {
                continue;
            }

            trackerBySecond[second] ??= new List<TrackerEvent>();
            trackerBySecond[second].Add(trackerEvent);
        }
    }

    private void IndexGameEvents()
    {
        if (Replay.GameEvents == null)
        {
            return;
        }

        foreach (GameEvent gameEvent in Replay.GameEvents)
        {
            int second = gameEvent.TimeSpan.FloorSeconds();
            if ((uint)second >= (uint)TotalSeconds)
            {
                continue;
            }

            gameEventsBySecond[second] ??= new List<GameEvent>();
            gameEventsBySecond[second].Add(gameEvent);
        }
    }
}
