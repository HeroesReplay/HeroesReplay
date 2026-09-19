using System;
using System.IO;
using HeroesReplay.Core.Services.Status;
using Xunit;

namespace HeroesReplay.Tests.Unit.Status;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class SpectatorStatusStoreTests
{
    [Fact]
    public void Patch_RoundTripsThroughFile()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"heroesreplay-status-{Guid.NewGuid():N}.json"
        );
        try
        {
            var store = new SpectatorStatusStore(path);
            store.Patch(status =>
            {
                status.SpectatorRunning = true;
                status.Phase = "TimerDetected";
                status.Timer = "00:03:21";
                status.Map = "Cursed Hollow";
            });

            var read = store.Read();
            Assert.True(read.SpectatorRunning);
            Assert.Equal("TimerDetected", read.Phase);
            Assert.Equal("00:03:21", read.Timer);
            Assert.Equal("Cursed Hollow", read.Map);
            Assert.False(read.SnapshotStale);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Read_MissingFile_IsIdle()
    {
        var store = new SpectatorStatusStore(
            Path.Combine(Path.GetTempPath(), $"heroesreplay-missing-{Guid.NewGuid():N}.json")
        );
        var status = store.Read();
        Assert.False(status.SpectatorRunning);
        Assert.Equal("Idle", status.Phase);
    }
}
