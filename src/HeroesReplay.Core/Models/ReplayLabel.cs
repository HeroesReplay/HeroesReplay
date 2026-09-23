namespace HeroesReplay.Core.Models;

public static class ReplayLabel
{
    public static string MapAndRank(string map, string rank)
    {
        if (string.IsNullOrWhiteSpace(map))
        {
            return "a replay";
        }

        if (string.IsNullOrWhiteSpace(rank))
        {
            return map.Trim();
        }

        return map.Trim() + " (" + rank.Trim() + ")";
    }
}
