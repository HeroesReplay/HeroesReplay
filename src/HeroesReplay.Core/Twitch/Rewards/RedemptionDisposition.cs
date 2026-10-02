using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HeroesReplay.Core.Requests;

namespace HeroesReplay.Core.Twitch.Rewards;

public enum RedemptionEnd
{
    None,
    Fulfill,

    /// <summary>Written by older builds for a session that was not verified. Never sent.</summary>
    Refund,
}

/// <summary>
/// A redemption whose match was verified is fulfilled. Any other session leaves it
/// UNFULFILLED, because the replay stays queued and plays again (#169). This decision is
/// recorded locally by the spectator. It does not call Twitch.
/// </summary>
public static class RedemptionDisposition
{
    public static RedemptionEnd Decide(bool hasRedemption, bool verified)
    {
        if (!hasRedemption || !verified)
        {
            return RedemptionEnd.None;
        }

        return RedemptionEnd.Fulfill;
    }
}

/// <summary>One line of <see cref="RedemptionDispositionLog.FileName"/>.</summary>
public sealed record RedemptionDispositionLine(
    int ReplayId,
    RedemptionEnd End,
    Guid RedemptionId,
    Guid RewardId,
    string BroadcasterId
);

/// <summary>
/// <c>Data\redemption-dispositions.txt</c>: the spectator appends one line per finished
/// requested session, and <c>twitch connect</c> sends the fulfilled ones to Twitch. A line is
/// <c>replayId end redemptionId [rewardId broadcasterId]</c>. Older lines have only the first
/// three fields.
/// </summary>
public static class RedemptionDispositionLog
{
    public const string FileName = "redemption-dispositions.txt";

    private const string NoBroadcaster = "-";

    public static void Append(string path, int? replayId, RewardRequest request, RedemptionEnd end)
    {
        if (
            string.IsNullOrWhiteSpace(path)
            || end == RedemptionEnd.None
            || request == null
            || request.RedemptionId == Guid.Empty
        )
        {
            return;
        }

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string broadcaster = string.IsNullOrWhiteSpace(request.BroadcasterId)
            ? NoBroadcaster
            : request.BroadcasterId.Trim();
        string line =
            (replayId ?? 0).ToString(CultureInfo.InvariantCulture)
            + " "
            + end
            + " "
            + request.RedemptionId.ToString("D")
            + " "
            + request.RewardId.ToString("D")
            + " "
            + broadcaster
            + Environment.NewLine;
        File.AppendAllText(path, line);
    }

    public static IReadOnlyList<RedemptionDispositionLine> Read(string path)
    {
        var lines = new List<RedemptionDispositionLine>();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return lines;
        }

        foreach (string text in File.ReadAllLines(path))
        {
            if (TryParse(text, out RedemptionDispositionLine line))
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    public static bool TryParse(string text, out RedemptionDispositionLine line)
    {
        line = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (
            parts.Length < 3
            || !int.TryParse(
                parts[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int id
            )
            || !Enum.TryParse(parts[1], ignoreCase: false, out RedemptionEnd end)
            || !Enum.IsDefined(end)
            || !Guid.TryParse(parts[2], out Guid redemptionId)
        )
        {
            return false;
        }

        Guid rewardId = Guid.Empty;
        if (parts.Length > 3)
        {
            Guid.TryParse(parts[3], out rewardId);
        }

        string broadcaster = parts.Length > 4 && parts[4] != NoBroadcaster ? parts[4] : null;
        line = new RedemptionDispositionLine(id, end, redemptionId, rewardId, broadcaster);
        return true;
    }
}
