using System;
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
                $"{unit.PlayerControlledBy.Character} killed by {unit.UnitKilledBy?.Name}"
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
