using System.Text.RegularExpressions;

namespace HeroesReplay.Core.Services.HeroesProfile;

public static class MinReplayIdFile
{
    public static bool TryReplace(string json, int replayId, out string updated)
    {
        updated = json;
        if (string.IsNullOrEmpty(json) || replayId <= 0)
        {
            return false;
        }

        var regex = new Regex("(\"MinReplayId\"\\s*:\\s*)\\d+");
        if (!regex.IsMatch(json))
        {
            return false;
        }

        updated = regex.Replace(json, "${1}" + replayId, 1);
        return true;
    }
}
