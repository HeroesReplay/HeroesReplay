using System;
using HeroesReplay.Core.YouTube;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class UploadWaitLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 18, 13, 0, TimeSpan.Zero);

    [Fact]
    public void Wait_LogsTheFirstWaitAndEachChangeOfReasonOnly()
    {
        var waits = new UploadWaitLog();

        Assert.True(waits.Wait(@"C:\Data\Contexts\1\a.mp4", "insert-cap"));
        Assert.False(waits.Wait(@"C:\Data\Contexts\1\a.mp4", "insert-cap"));
        Assert.False(waits.Wait(@"c:\data\contexts\1\A.mp4", "insert-cap"));
        Assert.True(waits.Wait(@"C:\Data\Contexts\1\a.mp4", "publication-full"));
        Assert.False(waits.Wait(@"C:\Data\Contexts\1\a.mp4", "publication-full"));
    }

    [Fact]
    public void Clear_AndRetain_ForgetRecordingsThatNoLongerWait()
    {
        var waits = new UploadWaitLog();
        waits.Wait(@"C:\a.mp4", "insert-cap");
        waits.Wait(@"C:\b.mp4", "insert-cap");
        waits.Wait(@"C:\c.mp4", "publication-full");

        waits.Clear(@"C:\a.mp4");
        waits.Retain(new[] { @"C:\b.mp4" });

        Assert.Equal(1, waits.Count);
        Assert.True(waits.Wait(@"C:\a.mp4", "insert-cap"));
        Assert.True(waits.Wait(@"C:\c.mp4", "publication-full"));
    }

    [Fact]
    public void Describe_CountsEachReason_AndMainIsTheCommonest()
    {
        var waits = new UploadWaitLog();
        Assert.Equal("none", waits.Describe());
        Assert.Null(waits.Main());

        waits.Wait(@"C:\a.mp4", "insert-cap");
        waits.Wait(@"C:\b.mp4", "insert-cap");
        waits.Wait(@"C:\c.mp4", "publication-full");

        Assert.Equal("insert-cap 2, publication-full 1", waits.Describe());
        Assert.Equal("insert-cap", waits.Main());
    }

    [Fact]
    public void SummaryDue_AtMostOncePerInterval()
    {
        var waits = new UploadWaitLog();

        Assert.True(waits.SummaryDue(Now));
        Assert.False(waits.SummaryDue(Now.AddSeconds(10)));
        Assert.False(waits.SummaryDue(Now.AddMinutes(4)));
        Assert.True(waits.SummaryDue(Now + UploadWaitLog.SummaryInterval));
    }
}
