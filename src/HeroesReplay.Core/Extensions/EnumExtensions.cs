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
                GameType.UnrankedDraft => "Unranked Draft",
                _ => throw new NotImplementedException()
            };
        }

        public static string GetQueryValue(this GameRank gameRank) => Enum.GetName(typeof(GameRank), gameRank);

        public static string GetQueryValue(this GameMode gameMode) => Enum.GetName(typeof(GameMode), gameMode);
    }
}