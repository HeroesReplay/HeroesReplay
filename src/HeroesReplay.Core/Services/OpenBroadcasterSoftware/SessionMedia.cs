using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

public static class SessionMedia
{
    public static bool HasRequestor(LoadedReplay replay) =>
        replay?.RewardQueueItem?.Request != null;

    public static bool ShouldRecord(OBSSettings obs, LoadedReplay replay)
    {
        if (obs == null)
        {
            return false;
        }

        if (obs.RecordingEnabled)
        {
            return true;
        }

        return obs.RecordRequestedReplays && HasRequestor(replay);
    }

    public static bool ShouldWriteYouTubeEntry(YouTubeSettings youtube, LoadedReplay replay)
    {
        if (youtube == null)
        {
            return false;
        }

        if (youtube.Enabled)
        {
            return true;
        }

        return youtube.UploadRequestedReplays && HasRequestor(replay);
    }
}
