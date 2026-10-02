using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.YouTube;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class SessionMediaTests
{
    [Fact]
    public void ShouldStream_OffByDefault()
    {
        Assert.False(SessionMedia.ShouldStream(null));
        Assert.False(SessionMedia.ShouldStream(new OBSSettings()));
        Assert.False(SessionMedia.ShouldStream(new OBSSettings { StreamingEnabled = false }));
        Assert.True(SessionMedia.ShouldStream(new OBSSettings { StreamingEnabled = true }));
    }

    [Fact]
    public void ShouldRecord_OffUnlessRequested()
    {
        var obs = new OBSSettings { RecordingEnabled = false, RecordRequestedReplays = true };
        var auto = new LoadedReplay();
        var requested = new LoadedReplay
        {
            RewardQueueItem = new RewardQueueItem
            {
                Request = new RewardRequest { Login = "viewer" },
            },
        };

        Assert.False(SessionMedia.ShouldRecord(obs, auto));
        Assert.True(SessionMedia.ShouldRecord(obs, requested));
    }

    [Fact]
    public void ShouldRecord_GlobalOnRecordsAll()
    {
        var obs = new OBSSettings { RecordingEnabled = true, RecordRequestedReplays = false };
        Assert.True(SessionMedia.ShouldRecord(obs, new LoadedReplay()));
    }

    [Fact]
    public void AlreadyOnYouTube_DoesNotRecordOrWriteAnotherEntry()
    {
        var obs = new OBSSettings { RecordingEnabled = true };
        var youtube = new YouTubeSettings { Enabled = true };
        var replay = new LoadedReplay { ReplayId = 65389750, AlreadyOnYouTube = true };

        Assert.False(SessionMedia.ShouldRecord(obs, replay));
        Assert.False(SessionMedia.ShouldWriteYouTubeEntry(youtube, replay));
    }

    [Fact]
    public void ShouldWriteYouTubeEntry_OnlyRequestedWhenUploadRequested()
    {
        var youtube = new YouTubeSettings { Enabled = false, UploadRequestedReplays = true };
        var auto = new LoadedReplay();
        var requested = new LoadedReplay
        {
            RewardQueueItem = new RewardQueueItem
            {
                Request = new RewardRequest { Login = "viewer" },
            },
        };

        Assert.False(SessionMedia.ShouldWriteYouTubeEntry(youtube, auto));
        Assert.True(SessionMedia.ShouldWriteYouTubeEntry(youtube, requested));
    }

    [Fact]
    public void ReplayId_WithoutRecordAndUpload_DoesNotRecordOrWriteEntry()
    {
        var obs = new OBSSettings { RecordingEnabled = false, RecordRequestedReplays = true };
        var youtube = new YouTubeSettings { Enabled = false, UploadRequestedReplays = true };
        var replay = ReplayIdLoaded(recordAndUpload: false);

        Assert.False(SessionMedia.ShouldRecord(obs, replay));
        Assert.False(SessionMedia.ShouldWriteYouTubeEntry(youtube, replay));
    }

    [Fact]
    public void ReplayId_WithRecordAndUpload_RecordsAndWritesEntry()
    {
        var obs = new OBSSettings { RecordingEnabled = false, RecordRequestedReplays = true };
        var youtube = new YouTubeSettings { Enabled = false, UploadRequestedReplays = true };
        var replay = ReplayIdLoaded(recordAndUpload: true);

        Assert.True(SessionMedia.ShouldRecord(obs, replay));
        Assert.True(SessionMedia.ShouldWriteYouTubeEntry(youtube, replay));
    }

    [Fact]
    public void PolicyRefusal_BlocksAnOwnedRecordingAndTheYouTubeEntry()
    {
        var obs = new OBSSettings { RecordingEnabled = true };
        var youtube = new YouTubeSettings { Enabled = true };
        var replay = new LoadedReplay
        {
            ReplayId = 42,
            PolicyAllowsRecording = false,
            PolicyAllowsPublication = false,
        };

        Assert.False(SessionMedia.ShouldRecord(obs, replay));
        Assert.False(SessionMedia.ShouldWriteYouTubeEntry(youtube, replay));
    }

    [Fact]
    public void PolicyApproval_StillRequiresTheObsAndYouTubeSwitches()
    {
        var replay = new LoadedReplay
        {
            ReplayId = 42,
            PolicyAllowsRecording = true,
            PolicyAllowsPublication = true,
        };

        Assert.False(
            SessionMedia.ShouldRecord(new OBSSettings { RecordingEnabled = false }, replay)
        );
        Assert.True(SessionMedia.ShouldRecord(new OBSSettings { RecordingEnabled = true }, replay));
        Assert.False(
            SessionMedia.ShouldWriteYouTubeEntry(new YouTubeSettings { Enabled = false }, replay)
        );
        Assert.True(
            SessionMedia.ShouldWriteYouTubeEntry(new YouTubeSettings { Enabled = true }, replay)
        );
    }

    private static LoadedReplay ReplayIdLoaded(bool recordAndUpload) =>
        new()
        {
            RewardQueueItem = new RewardQueueItem
            {
                Request = new RewardRequest
                {
                    Login = "viewer",
                    ReplayId = 65268119,
                    RecordAndUpload = recordAndUpload,
                },
            },
        };
}
