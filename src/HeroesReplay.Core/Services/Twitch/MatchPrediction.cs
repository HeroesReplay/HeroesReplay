using System;

namespace HeroesReplay.Core.Services.Twitch;

public static class MatchPrediction
{
    public const string Blue = "Blue";
    public const string Red = "Red";
    public const int MinWindowSeconds = 30;
    public const int MaxWindowSeconds = 1800;
    public const int MaxTitleLength = 45;

    public static string TitleForMap(string map)
    {
        if (string.IsNullOrWhiteSpace(map))
        {
            return "Who wins?";
        }

        string title = map.Trim() + ": who wins?";
        if (title.Length <= MaxTitleLength)
        {
            return title;
        }

        return "Who wins?";
    }

    public static int WindowSeconds(TimeSpan configured)
    {
        int seconds = (int)Math.Round(configured.TotalSeconds);
        if (seconds < MinWindowSeconds)
        {
            return MinWindowSeconds;
        }

        if (seconds > MaxWindowSeconds)
        {
            return MaxWindowSeconds;
        }

        return seconds;
    }

    public static string OutcomeTitle(int team) => team == 0 ? Blue : Red;
}
