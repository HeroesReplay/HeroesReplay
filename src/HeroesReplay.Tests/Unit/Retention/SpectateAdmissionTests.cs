using System.Collections.Generic;
using HeroesReplay.Core.Retention;
using Xunit;

namespace HeroesReplay.Tests.Unit.Retention;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class SpectateAdmissionTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    /// <summary>What production saw on 2026-10-08: 287 GB free and about 61 GB waiting for upload.</summary>
    private const long PlentyFree = 287 * Gigabyte;
    private const long HighPending = 61 * Gigabyte;

    [Fact]
    public void MayRecord_SkipsRecordingWhenTheDiskBacklogSaysStopAndRecordsOnAWarning()
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
        Assert.False(SpectateAdmission.MayRecord(stop));
        Assert.Equal(DiskPressure.Warning, warning.Pressure);
        Assert.True(SpectateAdmission.MayRecord(warning));
        Assert.True(SpectateAdmission.MayRecord(room));
    }

    [Fact]
    public void MayRecord_SkipsRecordingWhenPendingUploadsReachTheWatermark()
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
        Assert.False(SpectateAdmission.MayRecord(decision));
    }

    /// <summary>
    /// #279: a ReplayId request was spectated without a recording because ordinary recordings
    /// waiting for a publish time had tripped the pending-bytes gate, with 287 GB free.
    /// </summary>
    [Fact]
    public void Admit_RecordsARequestWithHighPendingBytesAndPlentyOfFreeSpace()
    {
        int clears = 0;

        DiskAdmission admission = SpectateAdmission.Admit(
            requested: true,
            SpectateAdmission.DefaultWatermarks(),
            () => new DiskBacklogInput { FreeBytes = PlentyFree, PendingUploadBytes = HighPending },
            () =>
            {
                clears++;
                return new RetentionSweep();
            }
        );

        Assert.True(admission.MayRecord);
        Assert.Equal(DiskPressure.Warning, admission.Decision.Pressure);
        Assert.Equal(DiskBacklog.PendingBytesHigh, admission.Decision.Reason);
        Assert.Equal(DiskBacklog.PendingBytesGate, admission.Decision.Gate);
        Assert.True(admission.Decision.Requested);
        Assert.Equal(1, clears);
    }

    [Fact]
    public void Admit_SkipsARequestWhenFreeSpaceIsGenuinelyLow()
    {
        int clears = 0;

        DiskAdmission admission = SpectateAdmission.Admit(
            requested: true,
            SpectateAdmission.DefaultWatermarks(),
            () =>
                new DiskBacklogInput
                {
                    FreeBytes = SpectateAdmission.StopFreeBytes,
                    PendingUploadBytes = HighPending,
                },
            () =>
            {
                clears++;
                return new RetentionSweep();
            }
        );

        Assert.False(admission.MayRecord);
        Assert.Equal(DiskBacklog.FreeBytesLow, admission.Decision.Reason);
        Assert.Equal(DiskBacklog.FreeSpaceGate, admission.Decision.Gate);
        Assert.True(admission.Decision.Requested);
        Assert.Null(admission.Cleared);
        Assert.Equal(0, clears);
    }

    [Fact]
    public void Admit_SkipsAnOrdinaryReplayWithHighPendingBytes()
    {
        int measures = 0;

        DiskAdmission admission = SpectateAdmission.Admit(
            requested: false,
            SpectateAdmission.DefaultWatermarks(),
            () =>
            {
                measures++;
                return new DiskBacklogInput
                {
                    FreeBytes = PlentyFree,
                    PendingUploadBytes = HighPending,
                };
            },
            () => new RetentionSweep()
        );

        Assert.False(admission.MayRecord);
        Assert.Equal(DiskPressure.SkipRecording, admission.Decision.Pressure);
        Assert.Equal(DiskBacklog.PendingBytesHigh, admission.Decision.Reason);
        Assert.Equal(DiskBacklog.PendingBytesGate, admission.Decision.Gate);
        Assert.False(admission.Decision.Requested);
        Assert.NotNull(admission.Cleared);
        Assert.Equal(0, admission.Cleared.DeletedFiles);
        Assert.Equal(1, measures);
    }

    /// <summary>
    /// Stale ordinary recordings go first. When clearing them brings pending bytes under the
    /// stop watermark, the disk is measured again and an ordinary replay records.
    /// </summary>
    [Fact]
    public void Admit_ClearsStaleRecordingsFirstAndRecordsWhenThatMakesRoom()
    {
        var pending = new Queue<long>(new[] { HighPending, 10 * Gigabyte });

        DiskAdmission admission = SpectateAdmission.Admit(
            requested: false,
            SpectateAdmission.DefaultWatermarks(),
            () =>
                new DiskBacklogInput
                {
                    FreeBytes = PlentyFree,
                    PendingUploadBytes = pending.Dequeue(),
                },
            () => new RetentionSweep { DeletedFiles = 26, FreedBytes = 51 * Gigabyte }
        );

        Assert.True(admission.MayRecord);
        Assert.Equal(DiskBacklog.PendingBytesWarning, admission.Decision.Reason);
        Assert.Equal(10 * Gigabyte, admission.Measured.PendingUploadBytes);
        Assert.Equal(26, admission.Cleared.DeletedFiles);
        Assert.Empty(pending);
    }

    [Fact]
    public void Admit_DoesNotClearAnythingWhilePendingBytesAreUnderTheStopWatermark()
    {
        int clears = 0;

        DiskAdmission admission = SpectateAdmission.Admit(
            requested: false,
            SpectateAdmission.DefaultWatermarks(),
            () =>
                new DiskBacklogInput
                {
                    FreeBytes = PlentyFree,
                    PendingUploadBytes = SpectateAdmission.WarnPendingBytes,
                },
            () =>
            {
                clears++;
                return new RetentionSweep();
            }
        );

        Assert.True(admission.MayRecord);
        Assert.Equal(DiskBacklog.PendingBytesWarning, admission.Decision.Reason);
        Assert.Null(admission.Cleared);
        Assert.Equal(0, clears);
    }
}
