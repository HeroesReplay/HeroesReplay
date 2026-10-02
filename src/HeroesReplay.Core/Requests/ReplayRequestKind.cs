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
