using System;

namespace HeroesReplay.Core.Services.Twitch.Rewards;

public static class UnrankedDraftRewardTitles
{
    public static bool IsUnrankedDraft(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        string trimmed = title.Trim();
        if (trimmed.Contains("Unranked Draft", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return trimmed.EndsWith("(UD)", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith("(Rank UD)", StringComparison.OrdinalIgnoreCase);
    }
}
