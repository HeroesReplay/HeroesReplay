using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;

namespace HeroesReplay.Core.Analysis.Calculators;

public class DeathCalculator : IFocusCalculator
{
    private readonly AppSettings settings;
    private readonly IGameData gameData;

    public DeathCalculator(AppSettings settings, IGameData gameData)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
    }

    public void Contribute(ReplayTimeline timeline)
    {
        if (timeline == null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        foreach (Unit unit in timeline.Replay.Units)
        {
            if (!unit.TimeSpanDied.HasValue || unit.PlayerControlledBy == null)
            {
                continue;
            }

            if (gameData.GetUnitGroup(unit.Name) != Unit.UnitGroup.Hero)
            {
                continue;
            }

            // The game data puts Abathur's Symbiote in the Hero group, and it "dies" with no
            // killer every time it ends: 61 times in one game, each a 9.5 death on Abathur's body
            // (#234). Only a hero body (Hero*) dies, and not Ultimate Evolution's copy of another
            // player's hero.
            if (
                !unit.Name.StartsWith("Hero", StringComparison.OrdinalIgnoreCase)
                || IsCopyOfAnotherPlayersHero(unit, timeline.Replay.Players)
            )
            {
                continue;
            }

            if (unit.PlayerKilledBy != null && unit.PlayerKilledBy != unit.PlayerControlledBy)
            {
                continue;
            }

            timeline.Offer(
                unit.TimeSpanDied.Value,
                GetType(),
                unit,
                unit.PlayerControlledBy,
                settings.Weights.PlayerDeath,
                $"{unit.PlayerControlledBy.Character} killed by {unit.UnitKilledBy?.Name}",
                victim: unit
            );
        }

        OfferDeathsWithNoDeadUnit(timeline);
    }

    /// <summary>
    /// A hero the game counts as dead while no hero unit of theirs died: Leoric, who turns into
    /// his wraith, and Tyrael and Uther, whose unit lives on for a few seconds. No unit death
    /// shows these to this calculator or to the <see cref="KillCalculator"/>, so the camera never
    /// saw Leoric die: 336 deaths in 559 replays of the production corpus.
    /// </summary>
    private void OfferDeathsWithNoDeadUnit(ReplayTimeline timeline)
    {
        IReadOnlyList<HeroKill> kills = HeroKills.Read(timeline.Replay);
        if (kills == null)
        {
            return;
        }

        Player[] players = timeline.Replay.Players;
        foreach (HeroKill kill in kills)
        {
            if (kill.Body != null)
            {
                continue;
            }

            Player victim = players[kill.VictimIndex];
            Unit hero = null;
            foreach (Unit unit in timeline.AliveHeroesAt(kill.Time.FloorSeconds()))
            {
                if (unit?.PlayerControlledBy == victim)
                {
                    hero = unit;
                    break;
                }
            }

            if (hero == null)
            {
                continue;
            }

            bool byEnemy = false;
            foreach (int killer in kill.KillerIndexes)
            {
                byEnemy |= players[killer].Team != victim.Team;
            }

            timeline.Offer(
                kill.Time,
                GetType(),
                hero,
                victim,
                byEnemy ? settings.Weights.PlayerKill : settings.Weights.PlayerDeath,
                $"{victim.Character} killed",
                victim: hero
            );
        }
    }

    private static bool IsCopyOfAnotherPlayersHero(Unit unit, Player[] players)
    {
        foreach (Player player in players ?? Array.Empty<Player>())
        {
            if (
                player != unit.PlayerControlledBy
                && player.HeroUnits?.Count > 0
                && string.Equals(player.HeroUnits[0].Name, unit.Name, StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }
}
