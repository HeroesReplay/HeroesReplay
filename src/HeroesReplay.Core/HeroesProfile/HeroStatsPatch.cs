using System;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>How a replay's build and game type name a hero statistics file.</summary>
public static class HeroStatsPatch
{
    /// <summary>The major patch of a build, Heroes Profile's <c>major</c> timeframe: <c>2.57.0.98348</c> is <c>2.57</c>.</summary>
    public static string Major(string gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            return null;
        }

        string[] parts = gameVersion.Trim().Split('.');
        if (
            parts.Length < 2
            || !int.TryParse(parts[0], out int major)
            || !int.TryParse(parts[1], out int minor)
            || major < 0
            || minor < 0
        )
        {
            return null;
        }

        return major + "." + minor;
    }

    /// <summary>
    /// Heroes Profile's short game type code (<c>sl</c>, <c>qm</c>, <c>ar</c>, <c>ud</c>,
    /// <c>hl</c>, <c>tl</c>) for a display name or code, or null when it is not one.
    /// </summary>
    public static string GameTypeCode(string gameType)
    {
        if (string.IsNullOrWhiteSpace(gameType))
        {
            return null;
        }

        string compact = gameType.Trim().Replace(" ", string.Empty).ToLowerInvariant();
        return compact switch
        {
            "sl" or "stormleague" => "sl",
            "qm" or "quickmatch" => "qm",
            "ar" or "aram" => "ar",
            "ud" or "unrankeddraft" => "ud",
            "hl" or "heroleague" => "hl",
            "tl" or "teamleague" => "tl",
            _ => null,
        };
    }

    /// <summary>The file name of one patch and game type: <c>2.57-sl.json</c>. Null when either is unknown.</summary>
    public static string FileName(string patch, string gameTypeCode)
    {
        string major = Major(patch);
        string code = GameTypeCode(gameTypeCode);
        return major == null || code == null ? null : major + "-" + code + ".json";
    }

    /// <summary>The patch part of a file name made by <see cref="FileName"/>, or null.</summary>
    public static string PatchOfFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        int dash = fileName.IndexOf('-', StringComparison.Ordinal);
        return dash <= 0 ? null : Major(fileName.Substring(0, dash));
    }
}
