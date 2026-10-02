using System.Text.RegularExpressions;

namespace HeroesReplay.Core.HeroesProfile;

public static class MinReplayIdFile
{
    private const string Pattern = "(\"MinReplayId\"\\s*:\\s*)(\\d+)";

    public static bool TryReplace(string json, int replayId, out string updated)
    {
        updated = json;
        if (string.IsNullOrEmpty(json) || replayId <= 0)
        {
            return false;
        }

        var regex = new Regex(Pattern);
        if (!regex.IsMatch(json))
        {
            return false;
        }

        updated = regex.Replace(json, "${1}" + replayId, 1);
        return true;
    }

    public static bool TryRead(string json, out int replayId)
    {
        replayId = 0;
        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        Match match = Regex.Match(json, Pattern);
        return match.Success && int.TryParse(match.Groups[2].Value, out replayId) && replayId > 0;
    }

    // The zip default must not replace a higher cursor already on the machine.
    public static bool TryPreserveHigher(string incoming, string previous, out string updated)
    {
        updated = incoming;
        if (!TryRead(previous, out int previousId) || !TryRead(incoming, out int incomingId))
        {
            return false;
        }

        if (previousId <= incomingId)
        {
            return false;
        }

        return TryReplace(incoming, previousId, out updated);
    }
}
