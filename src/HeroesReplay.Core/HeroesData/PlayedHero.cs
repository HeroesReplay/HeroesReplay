using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Metadata;

namespace HeroesReplay.Core.HeroesData;

/// <summary>
/// The hero a replay player actually played (#348). The game spawns that hero's unit for the
/// player (<c>HeroDemonHunter</c> is Valla), and the unit name is the same in every game mode and
/// every game language, so it is read first: from the player's parsed hero units, else from the
/// <c>PlayerSpawned</c> stat event when units were not parsed. The lobby's hero attribute and hero
/// id are the hero the player had selected before the match. In ARAM the game then assigns a
/// different hero, so they are trusted only outside ARAM. The replay's character name is the hero
/// played, but in the uploader's game language.
/// </summary>
public static class PlayedHero
{
    /// <summary>
    /// The stat event the game writes when a player's hero spawns:
    /// <c>{"PlayerSpawned", [{{"Hero"}, "HeroLeoric"}], [{{"PlayerID"}, 1}]}</c>.
    /// </summary>
    internal const string PlayerSpawnedEvent = "PlayerSpawned";

    /// <summary>
    /// The catalog hero the player played: the spawned hero unit first, then (outside ARAM) the
    /// lobby attribute id and hero id, then the replay's character name. Null when none matches.
    /// </summary>
    public static Hero Find(IReadOnlyList<Hero> catalog, Replay replay, Player player)
    {
        if (player == null || catalog == null || catalog.Count == 0)
        {
            return null;
        }

        foreach (string unit in SpawnedUnits(replay, player))
        {
            Hero hero = ByUnit(catalog, unit);
            if (hero != null)
            {
                return hero;
            }
        }

        if (LobbyHeroIsPlayed(replay))
        {
            Hero lobby =
                HeroDraft.Find(catalog, player.HeroAttributeId)
                ?? HeroDraft.Find(catalog, player.HeroId);
            if (lobby != null)
            {
                return lobby;
            }
        }

        return HeroDraft.Find(catalog, player.Character);
    }

    /// <summary>
    /// The catalog's English name of the hero played, else the replay's character name, else
    /// (outside ARAM) the lobby attribute id. Null when the replay names nothing.
    /// </summary>
    public static string Name(IReadOnlyList<Hero> catalog, Replay replay, Player player)
    {
        if (player == null)
        {
            return null;
        }

        Hero hero = Find(catalog, replay, player);
        if (!string.IsNullOrWhiteSpace(hero?.Name))
        {
            return hero.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(player.Character))
        {
            return player.Character.Trim();
        }

        if (LobbyHeroIsPlayed(replay) && !string.IsNullOrWhiteSpace(player.HeroAttributeId))
        {
            return player.HeroAttributeId.Trim();
        }

        return null;
    }

    /// <summary>
    /// The catalog attribute id of the hero played, else (outside ARAM) the lobby attribute id.
    /// </summary>
    public static string AttributeId(IReadOnlyList<Hero> catalog, Replay replay, Player player)
    {
        if (player == null)
        {
            return null;
        }

        Hero hero = Find(catalog, replay, player);
        if (!string.IsNullOrWhiteSpace(hero?.AttributeId))
        {
            return hero.AttributeId;
        }

        return LobbyHeroIsPlayed(replay) ? player.HeroAttributeId : null;
    }

    /// <summary>
    /// The lobby attribute id and hero id are the hero the player selected before the match. In
    /// ARAM the game assigns another hero, so they name a hero nobody played there.
    /// </summary>
    internal static bool LobbyHeroIsPlayed(Replay replay) =>
        replay == null || replay.GameMode != GameMode.ARAM;

    /// <summary>
    /// The hero unit names spawned for the player, earliest first: the parsed hero units, then the
    /// <c>PlayerSpawned</c> stat event. A later unit can be another hero's (an Abathur clone), so
    /// the caller takes the first one the catalog knows.
    /// </summary>
    internal static IEnumerable<string> SpawnedUnits(Replay replay, Player player)
    {
        if (player?.HeroUnits != null)
        {
            foreach (
                Unit unit in player
                    .HeroUnits.Where(unit => !string.IsNullOrWhiteSpace(unit?.Name))
                    .OrderBy(unit => unit.TimeSpanBorn)
            )
            {
                yield return unit.Name;
            }
        }

        if (replay?.TrackerEvents == null)
        {
            yield break;
        }

        // The stat event's PlayerID is the player's slot plus one, as in the parser's own units.
        // A replay the parser did not fill the slots of has only its player list.
        Player[] slots =
            replay.PlayersWithOpenSlots != null
            && Array.IndexOf(replay.PlayersWithOpenSlots, player) >= 0
                ? replay.PlayersWithOpenSlots
                : replay.Players;
        if (slots == null)
        {
            yield break;
        }

        foreach (TrackerEvent trackerEvent in replay.TrackerEvents)
        {
            if (
                TryReadSpawn(trackerEvent, out string unit, out int playerId)
                && playerId >= 1
                && playerId <= slots.Length
                && ReferenceEquals(slots[playerId - 1], player)
            )
            {
                yield return unit;
            }
        }
    }

    private static bool TryReadSpawn(TrackerEvent trackerEvent, out string unit, out int playerId)
    {
        unit = null;
        playerId = 0;
        if (
            trackerEvent?.TrackerEventType != ReplayTrackerEvents.TrackerEventType.StatGameEvent
            || trackerEvent.Data?.dictionary == null
            || !trackerEvent.Data.dictionary.TryGetValue(0, out TrackerEventStructure name)
            || name?.blobText != PlayerSpawnedEvent
            || !trackerEvent.Data.dictionary.TryGetValue(1, out TrackerEventStructure strings)
            || !trackerEvent.Data.dictionary.TryGetValue(2, out TrackerEventStructure numbers)
        )
        {
            return false;
        }

        unit = FirstValue(strings)?.blobText;
        long? id = FirstValue(numbers)?.vInt;
        if (string.IsNullOrWhiteSpace(unit) || id == null)
        {
            return false;
        }

        playerId = (int)id.Value;
        return true;
    }

    /// <summary>The value of a stat event's first key and value pair.</summary>
    private static TrackerEventStructure FirstValue(TrackerEventStructure pairs)
    {
        TrackerEventStructure[] array = pairs?.optionalData?.array;
        if (array == null || array.Length == 0 || array[0]?.dictionary == null)
        {
            return null;
        }

        return array[0].dictionary.TryGetValue(1, out TrackerEventStructure value) ? value : null;
    }

    /// <summary>The hero whose unit this is: its main unit first, then its other hero units.</summary>
    private static Hero ByUnit(IReadOnlyList<Hero> catalog, string unit)
    {
        foreach (Hero hero in catalog)
        {
            if (
                hero != null
                && string.Equals(hero.UnitId, unit, StringComparison.OrdinalIgnoreCase)
            )
            {
                return hero;
            }
        }

        foreach (Hero hero in catalog)
        {
            if (
                hero?.HeroUnitIds != null
                && hero.HeroUnitIds.Contains(unit, StringComparer.OrdinalIgnoreCase)
            )
            {
                return hero;
            }
        }

        return null;
    }
}
