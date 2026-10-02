using System;
using HeroesReplay.Core.YouTube.Outbox;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Outbox;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class UploadDispatchTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ForFile_SimulatesADryRun()
    {
        UploadAttemptResult result = UploadDispatch.ForFile(
            "replay-65389756",
            65389756,
            @"C:\replays\65389756.mp4",
            128,
            "len-128",
            youtubeEnabled: true,
            dryRun: true,
            At
        );

        Assert.True(result.Succeeded);
        Assert.Equal(UploadAttemptState.DryRunSimulated, result.Manifest.State);
        Assert.True(UploadAttemptReceipt.IsSimulation(result.Manifest));
    }

    [Fact]
    public void ForFile_DisablesBeforeItSimulates()
    {
        UploadAttemptResult result = UploadDispatch.ForFile(
            "replay-1",
            1,
            @"C:\replays\1.mp4",
            8,
            "len-8",
            youtubeEnabled: false,
            dryRun: true,
            At
        );

        Assert.True(result.Succeeded);
        Assert.Equal(UploadAttemptState.Disabled, result.Manifest.State);
    }

    [Fact]
    public void ForFile_StartsAnUploadWhenYouTubeIsLive()
    {
        UploadAttemptResult result = UploadDispatch.ForFile(
            "replay-2",
            2,
            @"C:\replays\2.mp4",
            8,
            "len-8",
            youtubeEnabled: true,
            dryRun: false,
            At
        );

        Assert.True(result.Succeeded);
        Assert.Equal(UploadAttemptState.Uploading, result.Manifest.State);
    }

    [Fact]
    public void ForFile_RejectsAnEmptyRecording()
    {
        UploadAttemptResult result = UploadDispatch.ForFile(
            "replay-3",
            3,
            @"C:\replays\3.mp4",
            0,
            "len-0",
            youtubeEnabled: true,
            dryRun: false,
            At
        );

        Assert.False(result.Succeeded);
        Assert.Equal(UploadAttemptReasons.MediaIncomplete, result.Reason);
    }

    [Fact]
    public void ForFile_RejectsAClockThatIsNotUtc()
    {
        UploadAttemptResult result = UploadDispatch.ForFile(
            "replay-4",
            4,
            @"C:\replays\4.mp4",
            8,
            "len-8",
            youtubeEnabled: true,
            dryRun: false,
            At.ToOffset(TimeSpan.FromHours(1))
        );

        Assert.False(result.Succeeded);
        Assert.Equal(UploadAttemptReasons.ClockNotUtc, result.Reason);
    }
}
