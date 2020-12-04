﻿using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class BossCampWeightings : IGameWeightings
	{
		public IEnumerable<(Unit Unit, Player Player, double Points, string Description)> GetPlayers(TimeSpan now, Replay replay)
		{
			var events = replay.TrackerEvents.Where(trackerEvent => now == trackerEvent.TimeSpan && trackerEvent.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent && trackerEvent.Data.dictionary[0].blobText == "JungleCampCapture");

			foreach (TrackerEvent capture in events)
			{
				int teamId = (int)capture.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value - 1;

				foreach (Unit unit in replay.Units.Where(unit => unit.TimeSpanDied < capture.TimeSpan && unit.TimeSpanBorn < capture.TimeSpan && unit.PlayerKilledBy != null && unit.PlayerKilledBy.Team == teamId && unit.TimeSpanDied > capture.TimeSpan.Subtract(TimeSpan.FromSeconds(10))))
				{
					yield return (unit, unit.PlayerKilledBy, Constants.Weights.BossCapture, $"{unit.PlayerKilledBy.HeroId} captured {unit.Name} (CampCaptures)");
				}
			}
		}
	}
}