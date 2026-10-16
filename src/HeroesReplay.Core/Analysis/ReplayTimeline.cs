using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Analysis.Calculators;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Spectating;

namespace HeroesReplay.Core.Analysis;

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
        string description,
        Unit victim = null
    )
    {
        if (unit == null || target == null)
        {
            return;
        }

        Offer(
            time.FloorSeconds(),
            new Focus(calculator, unit, target, points, description) { Victim = victim }
        );
    }

    /// <summary>
    /// One event over several seconds: every second it wins holds the same <see cref="Focus"/>, so
    /// <see cref="ApplyDeathContext"/> sees one event, not one per second.
    /// </summary>
    public void OfferRange(
        TimeSpan start,
        TimeSpan end,
        Type calculator,
        Unit unit,
        Player target,
        float points,
        string description,
        Unit victim = null
    )
    {
        int startSecond = start.FloorSeconds();
        int endSecond = end.FloorSeconds();
        if (endSecond < startSecond || unit == null || target == null)
        {
            return;
        }

        var focus = new Focus(calculator, unit, target, points, description) { Victim = victim };
        for (int second = startSecond; second <= endSecond && second < TotalSeconds; second++)
        {
            Offer(second, focus);
        }
    }

    private void Offer(int second, Focus focus)
    {
        if ((uint)second >= (uint)TotalSeconds)
        {
            return;
        }

        Focus existing = slots[second];
        if (existing == null || existing.Points < focus.Points)
        {
            slots[second] = focus;
        }
    }

    /// <summary>
    /// The story around each kill and death (#431). Before it, the lead-in follows the hero who dies,
    /// from <see cref="SpectateSettings.PastDeathContextTime"/> up to
    /// <see cref="SpectateSettings.MaxPastDeathContextTime"/> before the death; the more the death
    /// matters, the longer. A death matters by its respawn timer (full at
    /// <see cref="SpectateSettings.DeathContextFullRespawn"/>), and fully when something worth
    /// <see cref="WeightSettings.DeathImpact"/> happens while the hero is dead: an objective, a boss,
    /// a town hall, the core, a fight of eight heroes (a solo-lane duel is not one). The last
    /// <see cref="SpectateSettings.PastDeathContextTime"/> weighs as much as the death; before
    /// that the weight fades to <see cref="WeightSettings.DeathLeadInFloor"/> at the start of the
    /// lead-in, so a fight or an objective elsewhere early in it keeps the camera, though a
    /// solo-lane duel never does. After the death the event holds for
    /// <see cref="SpectateSettings.PresentDeathContextTime"/>.
    /// </summary>
    public void ApplyDeathContext(SpectateSettings spectate, WeightSettings weights)
    {
        if (spectate == null)
        {
            return;
        }

        int shortest = Math.Max(0, (int)spectate.PastDeathContextTime.TotalSeconds);
        int present = Math.Max(0, (int)spectate.PresentDeathContextTime.TotalSeconds);
        var deaths = new List<(int Second, Focus Focus, int LeadIn)>();
        var seen = new HashSet<Focus>();

        for (int second = 0; second < TotalSeconds; second++)
        {
            Focus focus = slots[second];
            if (focus != null && IsDeath(focus) && seen.Add(focus))
            {
                deaths.Add((second, focus, LeadInSeconds(second, focus, spectate, weights)));
            }
        }

        foreach ((int second, Focus focus, int leadIn) in deaths)
        {
            ApplyLeadIn(second, focus, leadIn, shortest, weights?.DeathLeadInFloor ?? 0f);

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

    private static bool IsDeath(Focus focus) =>
        focus.Calculator == typeof(KillCalculator) || focus.Calculator == typeof(DeathCalculator);

    private static bool IsDuel(Focus focus) => focus.Calculator == typeof(SoloLaneCalculator);

    /// <summary>A fight between heroes, a solo-lane duel included.</summary>
    private static bool IsFight(Focus focus) =>
        focus.Calculator == typeof(NearEnemyCalculator) || IsDuel(focus);

    private int LeadInSeconds(
        int second,
        Focus focus,
        SpectateSettings spectate,
        WeightSettings weights
    )
    {
        int shortest = Math.Max(0, (int)spectate.PastDeathContextTime.TotalSeconds);
        int longest = Math.Max(shortest, (int)spectate.MaxPastDeathContextTime.TotalSeconds);
        Unit victim = focus.Victim ?? focus.Unit;
        if (longest == shortest || victim == null)
        {
            return shortest;
        }

        int respawn = RespawnSecond(victim, second);
        double full = spectate.DeathContextFullRespawn.TotalSeconds;
        double importance = full > 0 ? Math.Clamp((respawn - second) / full, 0, 1) : 0;

        float impact = weights?.DeathImpact ?? 0f;
        if (importance < 1 && impact > 0)
        {
            for (int t = second + 1; t <= respawn && t < TotalSeconds; t++)
            {
                Focus other = slots[t];
                if (other != null && !IsDeath(other) && !IsDuel(other) && other.Points >= impact)
                {
                    importance = 1;
                    break;
                }
            }
        }

        return shortest + (int)Math.Round((longest - shortest) * importance);
    }

    /// <summary>
    /// When the player's next life of the same hero starts, or the end of the replay when the hero
    /// does not come back.
    /// </summary>
    private int RespawnSecond(Unit victim, int died)
    {
        int respawn = TotalSeconds;
        foreach (Unit life in victim.PlayerControlledBy?.HeroUnits ?? new List<Unit>())
        {
            if (life == victim || !string.Equals(life.Name, victim.Name, StringComparison.Ordinal))
            {
                continue;
            }

            int born = life.TimeSpanBorn.FloorSeconds();
            if (born >= died && born < respawn)
            {
                respawn = born;
            }
        }

        return respawn;
    }

    /// <summary>
    /// The last <paramref name="hold"/> seconds before the death weigh as much as the death; the
    /// seconds before them fade to <paramref name="floor"/> at the start of the lead-in. Every
    /// second of it beats a solo-lane duel (#437): a death swings the camera, a duel never does.
    /// </summary>
    private void ApplyLeadIn(int second, Focus focus, int leadIn, int hold, float floor)
    {
        Unit victim = focus.Victim ?? focus.Unit;
        Player player = victim?.PlayerControlledBy ?? focus.Target;
        if (victim == null || player == null)
        {
            return;
        }

        floor = Math.Min(floor, focus.Points);
        for (int delta = 1; delta <= leadIn; delta++)
        {
            int t = second - delta;
            if (t < 0 || !victim.IsAliveAt(TimeSpan.FromSeconds(t)))
            {
                break;
            }

            float points =
                delta <= hold
                    ? focus.Points
                    : focus.Points - (focus.Points - floor) * (delta - hold) / (leadIn - hold);
            if (slots[t] == null || slots[t].Points < points || IsDuel(slots[t]))
            {
                slots[t] = new Focus(
                    focus.Calculator,
                    victim,
                    player,
                    points,
                    $"{player.Character} {delta}s before: {focus.Description}"
                )
                {
                    Victim = victim,
                };
            }
        }
    }

    public void HoldSelectionAcrossEmptySeconds()
    {
        Focus last = null;
        for (int second = 0; second < TotalSeconds; second++)
        {
            if (slots[second] != null)
            {
                last = slots[second];
            }
            else if (last != null)
            {
                slots[second] = last;
            }
        }
    }

    /// <summary>
    /// The camera stays on the hero it shows when another player's hero wins a second within
    /// <paramref name="distance"/> of it (#432): both are in the same view, and a swap only jerks the
    /// camera. Checked every second, so the camera moves once the winner is farther away or the
    /// hero it shows dies.
    /// </summary>
    public void KeepNearbySelection(double distance)
    {
        if (distance <= 0)
        {
            return;
        }

        Player shown = null;
        for (int second = 0; second < TotalSeconds; second++)
        {
            Focus winner = slots[second];
            if (winner == null)
            {
                continue;
            }

            if (shown != null && winner.Target != shown)
            {
                Unit kept = AliveHero(shown, second);
                double apart = double.MaxValue;
                if (
                    TryGetPoint(kept, second, out Point keptPoint)
                    && TryGetPoint(AliveHero(winner.Target, second), second, out Point wantedPoint)
                )
                {
                    apart = keptPoint.DistanceTo(wantedPoint);
                }

                if (apart <= distance)
                {
                    slots[second] = new Focus(
                        winner.Calculator,
                        kept,
                        shown,
                        winner.Points,
                        $"{winner.Description} (camera kept on {shown.Character}, {apart:0.0} away)"
                    )
                    {
                        Victim = winner.Victim,
                    };
                    continue;
                }
            }

            shown = winner.Target;
        }
    }

    /// <summary>
    /// Once the camera moves to a hero it stays there for at least <paramref name="dwell"/>, so
    /// two heroes far apart that score alike do not flick it between them (#435). A gap shorter
    /// than that, after which the shown hero wins again, keeps the camera on it too. Only an
    /// outcome worth at least <paramref name="margin"/> more than the shot cuts it short: a kill or
    /// a death (with its lead-in), or anything but a fight worth <paramref name="outcome"/> (a map
    /// objective, a boss, a town hall, the core). A fight never does, however big, and neither
    /// does a solo-lane duel, so a solo laner in a duel or working the lane keeps the camera
    /// through a 4v4 that has not decided anything yet. The lead-in of a hero who dies within
    /// <paramref name="dwell"/> cuts it too, or the death would happen off camera, and the shot
    /// ends when the hero it shows dies.
    /// </summary>
    public void KeepShotsForDwell(TimeSpan dwell, float margin, float outcome)
    {
        int shortest = Math.Max(0, (int)dwell.TotalSeconds);
        if (shortest <= 1)
        {
            return;
        }

        Player shown = null;
        Focus shot = null;
        int started = 0;
        for (int second = 0; second < TotalSeconds; second++)
        {
            Focus winner = slots[second];
            if (winner == null)
            {
                continue;
            }

            if (winner.Target == shown)
            {
                shot = winner;
                continue;
            }

            Unit kept =
                shown != null && shot.Unit.IsAliveAt(TimeSpan.FromSeconds(second))
                    ? AliveHero(shown, second)
                    : null;
            if (
                kept != null
                && !(IsOutcome(winner, outcome) && winner.Points >= shot.Points + margin)
                && !LeadsIntoDeathWithin(winner, second, shortest)
                && (second - started < shortest || WinsAgainWithin(shown, second, shortest))
            )
            {
                slots[second] = new Focus(
                    shot.Calculator,
                    kept,
                    shown,
                    shot.Points,
                    $"{shot.Description} (camera held on {shown.Character} over {winner.Description})"
                )
                {
                    Victim = shot.Victim,
                };
                continue;
            }

            shown = winner.Target;
            shot = winner;
            started = second;
        }
    }

    /// <summary>
    /// A kill or a death (with its lead-in), or anything but a fight worth at least
    /// <paramref name="objective"/>: a map objective, a boss, a town hall, the core.
    /// </summary>
    private static bool IsOutcome(Focus focus, float objective) =>
        IsDeath(focus) || (!IsFight(focus) && focus.Points >= objective);

    private bool WinsAgainWithin(Player player, int second, int seconds)
    {
        for (int t = second + 1; t < second + seconds && t < TotalSeconds; t++)
        {
            if (slots[t]?.Target == player)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The focus is a lead-in: it follows a hero who is alive now and dies within
    /// <paramref name="seconds"/>.
    /// </summary>
    private static bool LeadsIntoDeathWithin(Focus focus, int second, int seconds)
    {
        if (
            focus.Victim == null
            || focus.Unit != focus.Victim
            || !focus.Victim.TimeSpanDied.HasValue
        )
        {
            return false;
        }

        int died = focus.Victim.TimeSpanDied.Value.FloorSeconds();
        return died > second && died - second <= seconds;
    }

    public void ApplyPlayerPriority(int playerIndex)
    {
        Player[] players = Replay.Players ?? Array.Empty<Player>();
        if (playerIndex < 0 || playerIndex >= players.Length)
        {
            return;
        }

        Player player = players[playerIndex];
        for (int second = 0; second < TotalSeconds; second++)
        {
            Unit hero = AliveHero(player, second);
            if (!PlayerPriorityRequest.Watch(hero != null))
            {
                continue;
            }

            Focus current = slots[second];
            slots[second] = new Focus(
                typeof(PlayerPriorityRequest),
                hero,
                player,
                current?.Points ?? 1,
                "player priority"
            );
        }
    }

    private Unit AliveHero(Player player, int second)
    {
        foreach (Unit unit in AliveHeroesAt(second))
        {
            if (unit?.PlayerControlledBy == player)
            {
                return unit;
            }
        }

        return null;
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
            )
            {
                Victim = focus.Victim,
            };
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
