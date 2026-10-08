using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using HeroesReplay.Core.Obs.Recording;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Recording;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class RecordingDiscardTests
{
    [Fact]
    public async Task DeleteAsync_WaitsForOBSToReleaseTheRecording()
    {
        // 2026-10-02 replay 65659620: the delete ran the moment OBS reported the output
        // stopped, hit "being used by another process", and left the mp4 on disk.
        string path = Path.Combine(Path.GetTempPath(), $"discard-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(path, "recording");
        var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var waits = new List<TimeSpan>();

        // OBS lets go of the file during the first pause. A timer that released it after 200 ms
        // raced 20 x 50 ms of retries on a busy machine (#331).
        bool deleted = await RecordingDiscard.DeleteAsync(
            path,
            NullLogger.Instance,
            attempts: 20,
            retryDelay: TimeSpan.FromMilliseconds(50),
            wait: delay =>
            {
                waits.Add(delay);
                held.Dispose();
                return Task.CompletedTask;
            }
        );

        Assert.True(deleted);
        Assert.False(File.Exists(path));
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(50) }, waits);
    }

    [Fact]
    public async Task DeleteAsync_GivesUpWhenTheFileStaysLocked()
    {
        string path = Path.Combine(Path.GetTempPath(), $"discard-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(path, "recording");
        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                bool deleted = await RecordingDiscard.DeleteAsync(
                    path,
                    NullLogger.Instance,
                    attempts: 3,
                    retryDelay: TimeSpan.FromMilliseconds(10)
                );

                Assert.False(deleted);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DeleteAsync_NothingToDelete_IsFalse()
    {
        Assert.False(await RecordingDiscard.DeleteAsync(null, NullLogger.Instance));
        Assert.False(
            await RecordingDiscard.DeleteAsync(
                Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.mp4"),
                NullLogger.Instance
            )
        );
    }
}
