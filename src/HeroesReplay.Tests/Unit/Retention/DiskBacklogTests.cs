using HeroesReplay.Core.Services.Retention;
using Xunit;

namespace HeroesReplay.Tests.Unit.Retention;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class DiskBacklogTests
{
    private const long WarnFree = 20;
    private const long StopFree = 8;
    private const long WarnPending = 40;
    private const long StopPending = 80;

    private static DiskBacklogSettings Settings()
    {
        return new DiskBacklogSettings
        {
            WarnWhenFreeBytesBelow = WarnFree,
            StopWhenFreeBytesBelow = StopFree,
            WarnWhenPendingBytesAtLeast = WarnPending,
            StopWhenPendingBytesAtLeast = StopPending,
        };
    }

    [Fact]
    public void PlentyOfSpace_KeepsSpectating()
    {
        DiskBacklogDecision decision = DiskBacklog.Evaluate(
            new DiskBacklogInput { FreeBytes = 100, PendingUploadBytes = 1 },
            Settings()
        );

        Assert.Equal(DiskPressure.Normal, decision.Pressure);
        Assert.Equal(DiskBacklog.WithinBudget, decision.Reason);
    }

    [Fact]
    public void FreeBytes_WarnsAtTheWatermark_AndStopsAtTheLowerOne()
    {
        DiskBacklogDecision warn = DiskBacklog.Evaluate(
            new DiskBacklogInput { FreeBytes = WarnFree, PendingUploadBytes = 0 },
            Settings()
        );
        DiskBacklogDecision justAboveStop = DiskBacklog.Evaluate(
            new DiskBacklogInput { FreeBytes = StopFree + 1, PendingUploadBytes = 0 },
            Settings()
        );
        DiskBacklogDecision stop = DiskBacklog.Evaluate(
            new DiskBacklogInput { FreeBytes = StopFree, PendingUploadBytes = 0 },
            Settings()
        );

        Assert.Equal(DiskPressure.Warning, warn.Pressure);
        Assert.Equal(DiskBacklog.FreeBytesWarning, warn.Reason);
        Assert.Equal(DiskPressure.Warning, justAboveStop.Pressure);
        Assert.Equal(DiskPressure.SkipRecording, stop.Pressure);
        Assert.Equal(DiskBacklog.FreeBytesLow, stop.Reason);
    }

    [Fact]
    public void PendingBytes_WarnsThenStops()
    {
        DiskBacklogDecision warn = DiskBacklog.Evaluate(
            new DiskBacklogInput { FreeBytes = 100, PendingUploadBytes = WarnPending },
            Settings()
        );
        DiskBacklogDecision stop = DiskBacklog.Evaluate(
            new DiskBacklogInput { FreeBytes = 100, PendingUploadBytes = StopPending },
            Settings()
        );

        Assert.Equal(DiskPressure.Warning, warn.Pressure);
        Assert.Equal(DiskBacklog.PendingBytesWarning, warn.Reason);
        Assert.Equal(DiskPressure.SkipRecording, stop.Pressure);
        Assert.Equal(DiskBacklog.PendingBytesHigh, stop.Reason);
    }

    [Fact]
    public void LowFreeBytes_WinsOverAPendingWarning()
    {
        DiskBacklogDecision decision = DiskBacklog.Evaluate(
            new DiskBacklogInput { FreeBytes = StopFree, PendingUploadBytes = WarnPending },
            Settings()
        );

        Assert.Equal(DiskPressure.SkipRecording, decision.Pressure);
        Assert.Equal(DiskBacklog.FreeBytesLow, decision.Reason);
    }

    [Theory]
    [InlineData(-1L, 0L)]
    [InlineData(10L, -1L)]
    public void NegativeReading_Stops(long free, long pending)
    {
        DiskBacklogDecision decision = DiskBacklog.Evaluate(
            new DiskBacklogInput { FreeBytes = free, PendingUploadBytes = pending },
            Settings()
        );

        Assert.Equal(DiskPressure.SkipRecording, decision.Pressure);
        Assert.Equal(DiskBacklog.MalformedInput, decision.Reason);
    }

    [Fact]
    public void MissingInput_Stops()
    {
        DiskBacklogDecision decision = DiskBacklog.Evaluate(null, Settings());

        Assert.Equal(DiskPressure.SkipRecording, decision.Pressure);
        Assert.Equal(DiskBacklog.MalformedInput, decision.Reason);
    }

    [Fact]
    public void BackwardsWatermarks_StopAsInvalidConfiguration()
    {
        DiskBacklogSettings backwards = Settings();
        backwards.WarnWhenFreeBytesBelow = StopFree;
        backwards.StopWhenFreeBytesBelow = WarnFree;

        DiskBacklogDecision decision = DiskBacklog.Evaluate(
            new DiskBacklogInput { FreeBytes = 100, PendingUploadBytes = 0 },
            backwards
        );

        Assert.Equal(DiskPressure.SkipRecording, decision.Pressure);
        Assert.Equal(DiskBacklog.ConfigurationInvalid, decision.Reason);
    }

    [Fact]
    public void NegativeThreshold_IsInvalid()
    {
        DiskBacklogSettings settings = Settings();
        settings.StopWhenPendingBytesAtLeast = -5;

        DiskBacklogDecision decision = DiskBacklog.Evaluate(
            new DiskBacklogInput { FreeBytes = 100, PendingUploadBytes = 0 },
            settings
        );

        Assert.Equal(DiskBacklog.ConfigurationInvalid, decision.Reason);
    }

    [Fact]
    public void ZeroThreshold_DisablesThatSignal()
    {
        DiskBacklogDecision decision = DiskBacklog.Evaluate(
            new DiskBacklogInput { FreeBytes = 0, PendingUploadBytes = long.MaxValue },
            new DiskBacklogSettings()
        );

        Assert.Equal(DiskPressure.Normal, decision.Pressure);
        Assert.Equal(DiskBacklog.WithinBudget, decision.Reason);
    }

    [Fact]
    public void WarnWithoutStop_DoesNotStopAtZeroFree()
    {
        DiskBacklogDecision decision = DiskBacklog.Evaluate(
            new DiskBacklogInput { FreeBytes = 0, PendingUploadBytes = 0 },
            new DiskBacklogSettings { WarnWhenFreeBytesBelow = WarnFree }
        );

        Assert.Equal(DiskPressure.Warning, decision.Pressure);
        Assert.Equal(DiskBacklog.FreeBytesWarning, decision.Reason);
    }
}
