using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class SessionMediaTests
{
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
}
