namespace HeroesReplay.Core.Models;

public static class ReplayRequestKind
{
    public static bool ViewerEnteredReplayId(RewardQueueItem item)
    {
        return item?.Request?.ReplayId is int id && id > 0;
    }

    public static bool ViewerEnteredReplayId(LoadedReplay loaded)
    {
        return ViewerEnteredReplayId(loaded?.RewardQueueItem);
    }
}
