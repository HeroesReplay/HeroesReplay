using HeroesReplay.Core.Services.Retention;
using Xunit;

namespace HeroesReplay.Tests.Unit.Retention;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class SpectateAdmissionTests
{
    [Fact]
    public void MayStart_StopsWhenTheDiskBacklogSaysStopAndContinuesOnAWarning()
    {
        DiskBacklogSettings settings = SpectateAdmission.DefaultWatermarks();
        DiskBacklogDecision stop = SpectateAdmission.Evaluate(
            new DiskBacklogInput
            {
                FreeBytes = SpectateAdmission.StopFreeBytes,
                PendingUploadBytes = 0,
            },
            settings
        );
        DiskBacklogDecision warning = SpectateAdmission.Evaluate(
            new DiskBacklogInput
            {
                FreeBytes = SpectateAdmission.WarnFreeBytes,
                PendingUploadBytes = 0,
            },
            settings
        );
        DiskBacklogDecision room = SpectateAdmission.Evaluate(
            new DiskBacklogInput
            {
                FreeBytes = SpectateAdmission.WarnFreeBytes + 1,
                PendingUploadBytes = 0,
            },
            settings
        );

        Assert.Equal(DiskBacklog.FreeBytesLow, stop.Reason);
        Assert.False(SpectateAdmission.MayStart(stop));
        Assert.Equal(DiskPressure.Warning, warning.Pressure);
        Assert.True(SpectateAdmission.MayStart(warning));
        Assert.True(SpectateAdmission.MayStart(room));
    }

    [Fact]
    public void MayStart_StopsWhenPendingUploadsReachTheWatermark()
    {
        DiskBacklogDecision decision = SpectateAdmission.Evaluate(
            new DiskBacklogInput
            {
                FreeBytes = SpectateAdmission.WarnFreeBytes + 1,
                PendingUploadBytes = SpectateAdmission.StopPendingBytes,
            },
            SpectateAdmission.DefaultWatermarks()
        );

        Assert.Equal(DiskBacklog.PendingBytesHigh, decision.Reason);
        Assert.False(SpectateAdmission.MayStart(decision));
    }
}
