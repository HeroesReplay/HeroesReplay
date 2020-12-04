using Heroes.ReplayParser;

using System;
using System.Collections.Generic;

namespace HeroesReplay.Core
{
    public interface IGameWeightings
	{
		IEnumerable<(Unit Unit, Player Player, double Points, string Description)> GetPlayers(TimeSpan timeSpan, Replay replay);
	}
}