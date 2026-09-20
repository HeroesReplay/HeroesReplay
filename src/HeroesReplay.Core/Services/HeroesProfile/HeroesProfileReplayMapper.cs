using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.HeroesProfile.Client.Replay.Item;
using HeroesReplay.HeroesProfile.Client.Replays;

namespace HeroesReplay.Core.Services.HeroesProfile;

public static class HeroesProfileReplayMapper
{
    public static IEnumerable<HeroesProfileReplay> ToReplays(ReplaysGetResponse page)
    {
        if (page?.Replays == null)
        {
            return Enumerable.Empty<HeroesProfileReplay>();
        }

        return page.Replays.Select(ToReplay).Where(r => r != null && r.Id > 0);
    }

    public static HeroesProfileReplay ToReplay(ReplaysGetResponse_replays row)
    {
        if (row == null)
        {
            return null;
        }

        return new HeroesProfileReplay
        {
            Id = row.ReplayID ?? 0,
            Region = row.Region,
            Fingerprint = row.Fingerprint,
            Parsed = row.Parsed,
            Deleted = row.Deleted,
            GameType = row.GameType,
            GameVersion = row.GameVersion,
            Map = row.GameMap,
            GameDate = row.GameDate,
            Downloadable = row.Downloadable,
            Rank = ReadAdditionalString(row.AdditionalData, "rank"),
            LeagueTier = ReadAdditionalInt(row.AdditionalData, "league_tier"),
            AverageMmr = ReadAdditionalDouble(row.AdditionalData, "avg_mmr"),
        };
    }

    public static HeroesProfileReplay ToReplay(int replayId, WithReplayGetResponse detail)
    {
        if (detail == null)
        {
            return null;
        }

        return new HeroesProfileReplay
        {
            Id = replayId,
            Region = detail.Region,
            GameType = detail.GameType,
            Map = detail.GameMap?.Name ?? detail.GameMap?.SanitizedMapName,
            GameDate = detail.GameDate,
            Downloadable = detail.Downloadable,
            Rank = RankImage.FromAverageMmr(RankImage.AveragePlayerMmr(detail.Players)),
        };
    }

    private static string ReadAdditionalString(
        System.Collections.Generic.IDictionary<string, object> data,
        string key
    )
    {
        if (data == null || !data.TryGetValue(key, out object value) || value == null)
        {
            return null;
        }

        return value.ToString();
    }

    private static int? ReadAdditionalInt(
        System.Collections.Generic.IDictionary<string, object> data,
        string key
    )
    {
        if (data == null || !data.TryGetValue(key, out object value) || value == null)
        {
            return null;
        }

        if (value is int i)
        {
            return i;
        }

        if (int.TryParse(value.ToString(), out int parsed))
        {
            return parsed;
        }

        return null;
    }

    private static double? ReadAdditionalDouble(
        System.Collections.Generic.IDictionary<string, object> data,
        string key
    )
    {
        if (data == null || !data.TryGetValue(key, out object value) || value == null)
        {
            return null;
        }

        if (value is double d)
        {
            return d;
        }

        if (double.TryParse(value.ToString(), out double parsed))
        {
            return parsed;
        }

        return null;
    }
}
