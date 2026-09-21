using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

public static class SessionMedia
{
    public static bool HasRequestor(LoadedReplay replay) =>
        replay?.RewardQueueItem?.Request != null;

    /// <summary>
    /// ReplayId (500) spectates only. ReplayId + YouTube (1000) records and writes
    /// youtube-entry.json. Other Twitch requests still record when requested.
    /// </summary>
    public static bool WantsRecording(LoadedReplay replay)
    {
        RewardRequest request = replay?.RewardQueueItem?.Request;
        if (request == null)
        {
            return false;
        }

        if (request.ReplayId.HasValue)
        {
            return request.RecordAndUpload;
        }

        return true;
    }

    public static bool ShouldStream(OBSSettings obs) => obs is { StreamingEnabled: true };

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

        return obs.RecordRequestedReplays && WantsRecording(replay);
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

        return youtube.UploadRequestedReplays && WantsRecording(replay);
    }
}
