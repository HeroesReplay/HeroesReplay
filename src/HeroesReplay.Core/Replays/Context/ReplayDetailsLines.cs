using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Replays.Context;

/// <summary>
/// The lines of the OBS replay details file: requestor, game type, bans, then the patch.
/// A line that is switched off or has no value is left out.
/// </summary>
public static class ReplayDetailsLines
{
    public static IReadOnlyList<string> Build(
        ReplayDetailsWriterSettings writer,
        LoadedReplay loaded,
        IReadOnlyDictionary<int, IReadOnlyCollection<string>> teamBans
    )
    {
        var lines = new List<string>();
        if (writer == null)
        {
            return lines;
        }

        string requestor = loaded?.RewardQueueItem?.Request?.Login;
        if (writer.Requestor && !string.IsNullOrWhiteSpace(requestor))
        {
            lines.Add($"Requestor: {requestor}");
        }

        string gameType = loaded?.HeroesProfileReplay?.GameType;
        if (writer.GameType && !string.IsNullOrWhiteSpace(gameType))
        {
            lines.Add(gameType);
        }

        if (writer.Bans)
        {
            lines.AddRange(Bans(teamBans));
        }

        string patch = Patch(loaded);
        if (writer.Patch && patch != null)
        {
            lines.Add($"Patch: {patch}");
        }

        return lines;
    }

    /// <summary>The replay file's own build, or the Heroes Profile game version when the file has none.</summary>
    public static string Patch(LoadedReplay loaded)
    {
        string version = loaded?.Replay?.ReplayVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            version = loaded?.HeroesProfileReplay?.GameVersion;
        }

        return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
    }

    private static IEnumerable<string> Bans(
        IReadOnlyDictionary<int, IReadOnlyCollection<string>> teamBans
    )
    {
        if (teamBans == null || !teamBans.Values.Any(bans => bans != null && bans.Any()))
        {
            yield break;
        }

        yield return "Bans:";
        foreach (int team in new[] { 0, 1 })
        {
            if (!teamBans.TryGetValue(team, out IReadOnlyCollection<string> bans) || bans == null)
            {
                continue;
            }

            foreach (string ban in bans.Where(ban => !string.IsNullOrWhiteSpace(ban)))
            {
                yield return $"T{team + 1}: {ban}";
            }
        }
    }
}
