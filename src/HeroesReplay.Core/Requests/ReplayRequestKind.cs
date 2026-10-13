using HeroesReplay.Core.Replays;

namespace HeroesReplay.Core.Requests;

public static class ReplayRequestKind
{
    public static bool ViewerEnteredReplayId(RewardRequest request)
    {
        return request?.ReplayId is int id && id > 0;
    }

    public static bool ViewerEnteredReplayId(RewardQueueItem item)
    {
        return ViewerEnteredReplayId(item?.Request);
    }

    public static bool ViewerEnteredReplayId(LoadedReplay loaded)
    {
        return ViewerEnteredReplayId(loaded?.RewardQueueItem);
    }

    /// <summary>What viewers see of a hidden rank: in chat, <c>!requests</c>, and the queue page.</summary>
    public const string HiddenRank = "rank hidden";

    /// <summary>A Blind Rating replay keeps its rank off the stream until the match ends (#411).</summary>
    public static bool Blind(RewardRequest request) => request?.Blind == true;

    public static bool Blind(RewardQueueItem item) => Blind(item?.Request);

    public static bool Blind(LoadedReplay loaded) => Blind(loaded?.RewardQueueItem);

    /// <summary>The rank viewers may see for a queued request: <see cref="HiddenRank"/> when blind.</summary>
    public static string ShownRank(RewardQueueItem item) =>
        Blind(item) ? HiddenRank : item?.HeroesProfileReplay?.Rank;

    /// <summary>
    /// Both ReplayId rewards ("ReplayId" and "ReplayId + YouTube") are recorded and uploaded
    /// with request priority (#165). A map, rank, or random reward is recorded only when its
    /// reward says so.
    /// </summary>
    public static bool RecordsAndUploads(RewardRequest request)
    {
        return request != null && (request.RecordAndUpload || ViewerEnteredReplayId(request));
    }
}
