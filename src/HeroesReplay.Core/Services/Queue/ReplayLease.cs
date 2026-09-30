using System;
using System.Globalization;
using System.IO;

namespace HeroesReplay.Core.Services.Queue;

/// <summary>
/// A taken replay is leased on disk before it is returned. A restart reads the same file.
/// A missing Heroes build is not leased.
/// </summary>
public static class ReplayLease
{
    public static bool Take(string path, int replayId)
    {
        if (replayId <= 0 || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!WorkEnvelope.TryTransition(WorkState.Ready, WorkState.Leased))
        {
            return false;
        }

        string id = replayId.ToString(CultureInfo.InvariantCulture);
        if (IsLease(DurableFile.ReadOrAside(path), id))
        {
            return true;
        }

        string body = "state=Leased" + Environment.NewLine + "replay=" + id + Environment.NewLine;
        DurableFile.Replace(path, body);
        return IsLease(DurableFile.ReadOrAside(path), id);
    }

    public static string PathFor(string dataDirectory, int replayId)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory) || replayId <= 0)
        {
            return null;
        }

        return Path.Combine(
            dataDirectory,
            "leases",
            replayId.ToString(CultureInfo.InvariantCulture) + ".txt"
        );
    }

    private static bool IsLease(string text, string id)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text.Contains("state=Leased", StringComparison.Ordinal)
            && text.Contains("replay=" + id, StringComparison.Ordinal);
    }
}
