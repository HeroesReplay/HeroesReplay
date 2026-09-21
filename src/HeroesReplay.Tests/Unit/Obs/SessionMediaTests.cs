using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
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
