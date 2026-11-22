using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis;

/// <summary>
/// One hero death the game itself recorded (a <c>PlayerDeath</c> stat event).
/// </summary>
/// <param name="Time">When the hero unit died, or the event's time when no unit died.</param>
/// <param name="VictimIndex">The dead player's index in the replay's players.</param>
/// <param name="KillerIndexes">Every player the game credits with the takedown.</param>
/// <param name="Point">Where the hero died, or null when the replay does not say.</param>
/// <param name="Body">
/// The victim's hero unit that died, or null when none did: Leoric turns into his wraith, and
/// Tyrael and Uther keep their unit for seconds after the death.
/// </param>
/// <param name="BlowIndex">The player who landed the killing blow, or -1 when it is not known.</param>
/// <param name="Partial">
/// The death is one body of several, or a part of a death on the scoreboard: one of The Lost
/// Vikings, or Murky.
/// </param>
public sealed record HeroKill(
    TimeSpan Time,
    int VictimIndex,
    IReadOnlyList<int> KillerIndexes,
    MapPoint? Point,
    Unit Body,
    int BlowIndex,
    bool Partial
);

/// <summary>
/// The hero deaths of a replay, read from its <c>PlayerDeath</c> stat events and not from the
/// deaths of hero units. A hero unit is not a hero: Chen's spirits, D.Va's mech and Abathur's
/// Ultimate Evolution copy die as hero units with no hero dying, and Leoric dies with no unit
/// dying at all. In 559 replays of the production corpus the events gave 19,038 deaths, each for a
/// known player; 96 % matched a hero unit that died within a second, and the rest were Leoric,
/// Tyrael and Uther.
/// <para>
/// The event names every player credited with the takedown, not the one who landed the blow. The
/// blow is the matched unit's killer, when that is an enemy player whose hero, vehicle or hero
/// form struck it. A summon's blow is not its owner's. With no matched unit the blow is known only
/// when a single enemy is credited.
/// </para>
/// </summary>
public static class HeroKills
{
    private const string PlayerDeathEvent = "PlayerDeath";

    /// <summary>A hero unit dies at most this long before or after its <c>PlayerDeath</c> event.</summary>
    private static readonly TimeSpan MatchWindow = TimeSpan.FromSeconds(1.5);

    private static readonly HashSet<string> PartialBodies = new(StringComparer.Ordinal)
    {
        "HeroOlaf",
        "HeroBaleog",
        "HeroErik",
        "HeroMurky",
    };

    /// <summary>
    /// The replay's hero deaths in time order, or null when the replay has no <c>PlayerDeath</c>
    /// events (a hand-built replay, or a build that did not write them), so the caller can fall
    /// back to hero unit deaths.
    /// </summary>
    public static IReadOnlyList<HeroKill> Read(Replay replay)
    {
        Player[] players = replay?.Players;
        if (players == null || replay.TrackerEvents == null)
        {
            return null;
        }

        var kills = new List<HeroKill>();
        var used = new HashSet<Unit>();
        bool any = false;
        foreach (TrackerEvent trackerEvent in replay.TrackerEvents)
        {
            if (
                trackerEvent?.TrackerEventType != ReplayTrackerEvents.TrackerEventType.StatGameEvent
                || trackerEvent.Data?.dictionary == null
                || !trackerEvent.Data.dictionary.TryGetValue(0, out TrackerEventStructure name)
                || name?.blobText != PlayerDeathEvent
            )
            {
                continue;
            }

            StatEvent stat = StatEvent.From(trackerEvent);
            if (stat == null)
            {
                continue;
            }

            any = true;
            int victimIndex = (stat.Int("PlayerID") ?? 0) - 1;
            if (victimIndex < 0 || victimIndex >= players.Length || players[victimIndex] == null)
            {
                continue;
            }

            Player victim = players[victimIndex];
            int[] killers = stat.Ints("KillingPlayer")
                .Select(id => id - 1)
                .Where(index => index >= 0 && index < players.Length && players[index] != null)
                .Distinct()
                .ToArray();
            Unit body = Body(victim, stat.Time, used);
            if (body != null)
            {
                used.Add(body);
            }

            MapPoint? point =
                body?.PointDied != null ? new MapPoint(body.PointDied.X, body.PointDied.Y)
                : stat.Int("PositionX") is int x && stat.Int("PositionY") is int y
                    ? new MapPoint(x, y)
                : null;
            kills.Add(
                new HeroKill(
                    body?.TimeSpanDied ?? stat.Time,
                    victimIndex,
                    killers,
                    point,
                    body,
                    Blow(players, victim, killers, body),
                    body != null && PartialBodies.Contains(body.Name ?? string.Empty)
                )
            );
        }

        if (!any)
        {
            return null;
        }

        return kills.OrderBy(kill => kill.Time).ThenBy(kill => kill.VictimIndex).ToArray();
    }

    /// <summary>
    /// The unit is a body its player fights in, so its blow is the player's own: a hero unit, a map
    /// vehicle (the Dragon Knight, the Triglav Protector, the Garden Terror) or a hero form
    /// (Ragnaros's Molten Core). A summon is not (Misha, Azmodan's demons, a Symbiote host).
    /// </summary>
    public static bool IsOwnBody(Player player, Unit unit)
    {
        if (player == null || unit == null)
        {
            return false;
        }

        if (player.HeroUnits?.Contains(unit) == true)
        {
            return true;
        }

        string name = unit.Name ?? string.Empty;
        return name.Contains("Vehicle", StringComparison.Ordinal)
            || name.Equals("RagnarosBigRag", StringComparison.Ordinal)
            || name.Equals("LongboatRaidBoat", StringComparison.Ordinal);
    }

    private static Unit Body(Player victim, TimeSpan time, HashSet<Unit> used)
    {
        Unit best = null;
        TimeSpan bestGap = TimeSpan.MaxValue;
        foreach (Unit unit in victim.HeroUnits ?? new List<Unit>())
        {
            if (unit?.TimeSpanDied == null || used.Contains(unit))
            {
                continue;
            }

            TimeSpan gap = (unit.TimeSpanDied.Value - time).Duration();
            if (gap <= MatchWindow && gap < bestGap)
            {
                best = unit;
                bestGap = gap;
            }
        }

        return best;
    }

    private static int Blow(Player[] players, Player victim, int[] killers, Unit body)
    {
        if (body != null)
        {
            Player killer = body.PlayerKilledBy;
            if (
                killer == null
                || ReferenceEquals(killer, victim)
                || killer.Team == victim.Team
                || (body.UnitKilledBy != null && !IsOwnBody(killer, body.UnitKilledBy))
            )
            {
                return -1;
            }

            return Array.IndexOf(players, killer);
        }

        int[] enemies = killers.Where(index => players[index].Team != victim.Team).ToArray();
        return enemies.Length == 1 ? enemies[0] : -1;
    }
}
