using System;
using System.IO;

namespace HeroesReplay.Core.Services.Twitch;

public enum RedemptionEnd
{
    None,
    Fulfill,
    Refund,
}

/// <summary>
/// A redemption with no verified match is a refund. This decision is recorded locally.
/// It does not call Twitch.
/// </summary>
public static class RedemptionDisposition
{
    public static RedemptionEnd Decide(bool hasRedemption, bool verified)
    {
        if (!hasRedemption)
        {
            return RedemptionEnd.None;
        }

        return verified ? RedemptionEnd.Fulfill : RedemptionEnd.Refund;
    }
}

public static class RedemptionDispositionLog
{
    public static void Append(string path, int? replayId, Guid redemptionId, RedemptionEnd end)
    {
        if (string.IsNullOrWhiteSpace(path) || end == RedemptionEnd.None)
        {
            return;
        }

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string line =
            (replayId ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " "
            + end
            + " "
            + redemptionId.ToString("D")
            + Environment.NewLine;
        File.AppendAllText(path, line);
    }
}
