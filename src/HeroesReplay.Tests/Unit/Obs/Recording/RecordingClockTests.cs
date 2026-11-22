using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Obs.Recording;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Recording;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class RecordingClockTests
{
    [Fact]
    public void TryMap_InterpolatesHudTimeOntoTheRecording()
    {
        (double Hud, double File)[] samples = { (0, 10), (60, 40), (120, 70) };

        Assert.True(RecordingClock.TryMap(samples, 30, 90, out double start, out double duration));

        Assert.Equal(25, start, 3);
        Assert.Equal(30, duration, 3);
    }

    [Fact]
    public void CutArguments_SeekAfterTheInputAndDoNotScale()
    {
        string[] args = FfmpegArguments.Cut(@"C:\match.mp4", @"C:\clip.mp4", 12.5, 20);

        Assert.Equal("-i", args[1]);
        Assert.Equal(@"C:\match.mp4", args[2]);
        Assert.Equal("-ss", args[3]);
        Assert.Equal("12.5", args[4]);
        Assert.Equal("-t", args[5]);
        Assert.Equal("20", args[6]);
        Assert.DoesNotContain("-vf", args);
        Assert.Equal(@"C:\clip.mp4", args[^1]);
    }

    [Fact]
    public void CutArguments_LateClip_SeekTheInputToTheLeadThenTheExactFrame()
    {
        string[] args = FfmpegArguments.Cut(@"C:\match.mp4", @"C:\clip.mp4", 1500.25, 20);

        Assert.Equal(
            new[] { "-y", "-ss", "1470.25", "-i", @"C:\match.mp4", "-ss", "30" },
            args[..7]
        );
        Assert.Equal("-t", args[7]);
        Assert.Equal("20", args[8]);
    }

    [Fact]
    public void DurationInside_ARecordingThatEndsFirst_ShortensTheClip()
    {
        Assert.Equal(20, FfmpegArguments.DurationInside(40, 20, 85.6));
        Assert.Equal(5.6, FfmpegArguments.DurationInside(80, 20, 85.6), 3);
    }

    [Fact]
    public void FitsRecording_RejectsARangePastTheEndOfTheFile()
    {
        Assert.False(FfmpegArguments.FitsRecording(1138, 42, 85.6));
        Assert.True(FfmpegArguments.FitsRecording(40, 20, 85.6));
    }
}
