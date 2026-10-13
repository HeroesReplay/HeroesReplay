using System;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Requests;

/// <summary>
/// The league a text names (#429): a rank reward's input ("Gold", "gold 3", "Plat", "GM") or a
/// Heroes Profile tier ("Platinum 2", "Grandmaster"). Grandmaster counts as Master, as in the
/// Blind Rating prediction (#411).
/// </summary>
public static class RankInput
{
    /// <summary>The ranks a viewer can enter, for replies: "Bronze, Silver, ... or Master".</summary>
    public static string Names
    {
        get
        {
            string[] names = Enum.GetNames<GameRank>();
            return string.Join(", ", names[..^1]) + " or " + names[^1];
        }
    }

    /// <summary>The league <paramref name="text"/> names, or null when it names none.</summary>
    public static GameRank? Read(string text)
    {
        string league = RankImage.Normalize(text, leagueTier: null);
        if (league == "grandmaster")
        {
            league = "master";
        }

        return league != null && Enum.TryParse(league, ignoreCase: true, out GameRank rank)
            ? rank
            : null;
    }
}
