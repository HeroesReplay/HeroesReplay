using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Analyzer.Calculators
{
    public class DeathCalculator : IFocusCalculator
    {
        private readonly IGameData gameData;
        private readonly WeightOptions weightOptions;
        private readonly SpectateOptions spectateOptions;

        public DeathCalculator(IOptions<WeightOptions> weightOptions, IOptions<SpectateOptions> spectateOptions, IGameData gameData)
        {
            this.gameData = gameData;
            this.weightOptions = weightOptions.Value;
            this.spectateOptions = spectateOptions.Value;
        }

        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null) 
                throw new ArgumentNullException(nameof(replay));

            foreach (var unit in replay.Units.Where(u => gameData.GetUnitGroup(u.Name) == Unit.UnitGroup.Hero && u.TimeSpanDied == now && (u.PlayerKilledBy == null || u.PlayerKilledBy == u.PlayerControlledBy) && u.PlayerControlledBy != null))
            {
                yield return new Focus(
                    GetType(),
                    unit, 
                    unit.PlayerControlledBy, 
                    weightOptions.PlayerDeath,
                    $"{unit.PlayerControlledBy.Character} killed by {unit.UnitKilledBy?.Name}");
            }
        }
    }
}