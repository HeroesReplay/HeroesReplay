using System;
using HeroesReplay.Core.HeroesProfile;

namespace HeroesReplay.Core.YouTube.Playlists;

/// <summary>
/// Current-patch videos go on one playlist. Older lines stay on an archive playlist.
/// Unknown builds get their own name. Nothing is deleted.
/// </summary>
public static class PatchPlaylist
{
    public const string Unknown = "Unknown patch";

    public static string Name(string replayVersion, string currentLine) =>
        Name(replayVersion, currentLine, seasonName: null);

    public static string Name(string replayVersion, string currentLine, string seasonName)
    {
        string line = GameVersionOrder.PatchLine(replayVersion);
        if (string.IsNullOrWhiteSpace(line))
        {
            return Unknown;
        }

        if (
            !string.IsNullOrWhiteSpace(currentLine)
            && string.Equals(line, currentLine.Trim(), StringComparison.Ordinal)
        )
        {
            return string.IsNullOrWhiteSpace(seasonName) ? "Patch " + line : seasonName.Trim();
        }

        return "Patch " + line + " archive";
    }

    public static bool MayFile(string privacyStatus)
    {
        return string.Equals(privacyStatus, "public", StringComparison.OrdinalIgnoreCase);
    }

    public static bool MayFile(YouTubeEntry entry)
    {
        if (entry == null)
        {
            return false;
        }

        return MayFile(entry.ActualPrivacyStatus) || MayFile(entry.PrivacyStatus);
    }
}
