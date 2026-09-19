using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Models;
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
        };
    }
}
