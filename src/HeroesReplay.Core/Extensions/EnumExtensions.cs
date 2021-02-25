using System;

using Heroes.ReplayParser;

using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Extensions
{
    public static class EnumExtensions
    {
        public static string GetQueryValue(this GameType gameType)
        {
            return gameType switch
            {
                GameType.ARAM => "ARAM",
                GameType.QuickMatch => "Quick Match",
                GameType.StormLeague => "Storm League",
                GameType.Unranked => "Unranked Draft",
                _ => throw new NotImplementedException()
            };
        }

        public static string GetQueryValue(this GameRank gameRank)
        {
            return gameRank switch
            {
                GameRank.Bronze => "bronze",
                GameRank.Silver => "silver",
                GameRank.Gold => "gold",
                GameRank.Platinum => "platinum",
                GameRank.Diamond => "diamond",
                GameRank.Master => "master",
                _ => throw new NotImplementedException()
            };
        }

        public static string GetQueryValue(this GameMode gameMode) => Enum.GetName(typeof(GameMode), gameMode);
    }
}