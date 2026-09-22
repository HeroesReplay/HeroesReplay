using System;
using System.IO;
using System.Threading.Tasks;
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
    public void Patch_RoundTripsMatchCompletion()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"heroesreplay-status-{Guid.NewGuid():N}.json"
        );
        try
        {
            var ended = new DateTimeOffset(2026, 9, 21, 19, 5, 0, TimeSpan.Zero);
            var store = new SpectatorStatusStore(path);
            store.Patch(status =>
            {
                status.Phase = "EndDetected";
                status.ReplayId = 65268379;
                status.CompletedAt = ended;
                status.CompletedReplayId = 65268379;
                status.CompletedWinnerTeam = 0;
            });

            var read = store.Read();
            Assert.Equal(ended, read.CompletedAt);
            Assert.Equal(65268379, read.CompletedReplayId);
            Assert.Equal(0, read.CompletedWinnerTeam);
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
    public async Task Patch_ReplacesFileWhileAReaderHoldsIt()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"heroesreplay-status-{Guid.NewGuid():N}.json"
        );
        try
        {
            var store = new SpectatorStatusStore(path);
            store.Patch(status => status.Phase = "Loading");

            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            Task write = Task.Run(() => store.Patch(status => status.Phase = "TimerDetected"));
            await Task.Delay(200);
            held.Dispose();

            await write.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("TimerDetected", store.Read().Phase);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (File.Exists(path + ".tmp"))
            {
                File.Delete(path + ".tmp");
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
