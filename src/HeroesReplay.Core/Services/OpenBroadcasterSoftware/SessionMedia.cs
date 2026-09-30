using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

public static class SessionMedia
{
    // Twitch stream markers only bookmark the live VOD, and clips are capped at 60 seconds.
    // Neither can publish one full match to YouTube. OBS records once the loading screen
    // or the HUD clock is visible, and stops before the report scenes. A session that
    // never shows the match clock is deleted instead of uploaded.

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
        if (obs == null || replay?.AlreadyOnYouTube == true)
        {
            return false;
        }

        if (replay?.PolicyAllowsRecording == false)
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
        if (youtube == null || replay?.AlreadyOnYouTube == true)
        {
            return false;
        }

        if (replay?.PolicyAllowsPublication == false)
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
