using System;
using HeroesReplay.Core.MediaPolicy;
using Xunit;

namespace HeroesReplay.Tests.Unit.MediaPolicy;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class RecordingCapTests
{
    [Fact]
    public void Capacity_IsWhatThePacingPublishesWithinTheHorizon()
    {
        // 6 a day less 2 for requests is 4, under 30/7 a week, over 3 days.
        Assert.Equal(12, RecordingCap.PublicationCapacity(Production(), 95));

        ReplayMediaPolicySettings fortnight = Production();
        fortnight.MaxPublishAhead = TimeSpan.FromDays(14);
        Assert.Equal(56, RecordingCap.PublicationCapacity(fortnight, 95));

        ReplayMediaPolicySettings sameDay = Production();
        sameDay.MaxPublishAhead = TimeSpan.Zero;
        Assert.Equal(4, RecordingCap.PublicationCapacity(sameDay, 95));

        // Uploads cap it too.
        Assert.Equal(6, RecordingCap.PublicationCapacity(Production(), 2));
    }

    [Theory]
    [InlineData(0, 0, true, RecordingCap.Room)]
    [InlineData(2, 9, true, RecordingCap.Room)]
    [InlineData(2, 10, false, RecordingCap.PublicationFull)]
    [InlineData(0, 12, false, RecordingCap.PublicationFull)]
    [InlineData(85, 0, false, RecordingCap.UploadBacklog)]
    public void Ordinary_IsRecordedOnlyWhileThePipelineHasRoom(
        int pending,
        int ahead,
        bool allow,
        string reason
    )
    {
        RecordingCapDecision decision = RecordingCap.Decide(
            Input(ReplayMediaPriority.Ordinary, pending, ahead),
            Production()
        );

        Assert.Equal(allow, decision.Allow);
        Assert.Equal(reason, decision.Reason);
    }

    [Theory]
    [InlineData(ReplayMediaPriority.Notable)]
    [InlineData(ReplayMediaPriority.HighSkill)]
    public void NotableAndHighSkill_AreCappedLikeOrdinary(ReplayMediaPriority priority)
    {
        RecordingCapDecision decision = RecordingCap.Decide(Input(priority, 5, 20), Production());

        Assert.False(decision.Allow);
        Assert.Equal(RecordingCap.PublicationFull, decision.Reason);
    }

    [Fact]
    public void Request_IsAlwaysRecorded()
    {
        RecordingCapDecision decision = RecordingCap.Decide(
            Input(ReplayMediaPriority.Requested, 500, 500),
            Production()
        );

        Assert.True(decision.Allow);
        Assert.Equal(RecordingCap.Requested, decision.Reason);
    }

    [Fact]
    public void DryRunOrSwitchedOff_DoesNotCap()
    {
        RecordingCapInput full = Input(ReplayMediaPriority.Ordinary, 90, 90);
        var dryRun = new RecordingCapInput
        {
            Priority = full.Priority,
            PendingUploads = full.PendingUploads,
            ScheduledAhead = full.ScheduledAhead,
            Live = false,
            PublicListing = true,
            UploadsPerDay = 80,
        };
        ReplayMediaPolicySettings off = Production();
        off.CapRecordingToPublication = false;

        Assert.Equal(RecordingCap.NotLive, RecordingCap.Decide(dryRun, Production()).Reason);
        Assert.Equal(RecordingCap.Off, RecordingCap.Decide(full, off).Reason);
        Assert.True(RecordingCap.Decide(full, off).Allow);
    }

    [Fact]
    public void PrivateListing_IsCappedByUploadsOnly()
    {
        var input = new RecordingCapInput
        {
            Priority = ReplayMediaPriority.Ordinary,
            PendingUploads = 3,
            ScheduledAhead = 0,
            Live = true,
            PublicListing = false,
            UploadsPerDay = 4,
        };

        Assert.True(RecordingCap.Decide(input, Production()).Allow);
        Assert.False(
            RecordingCap
                .Decide(
                    new RecordingCapInput
                    {
                        Priority = ReplayMediaPriority.Ordinary,
                        PendingUploads = 4,
                        Live = true,
                        UploadsPerDay = 4,
                    },
                    Production()
                )
                .Allow
        );
    }

    private static RecordingCapInput Input(ReplayMediaPriority priority, int pending, int ahead) =>
        new()
        {
            Priority = priority,
            PendingUploads = pending,
            ScheduledAhead = ahead,
            Live = true,
            PublicListing = true,
            UploadsPerDay = 80,
        };

    private static ReplayMediaPolicySettings Production() =>
        new()
        {
            Version = "1",
            RecordingMode = ReplayRecordingMode.Selected,
            PublicationMode = ReplayPublicationMode.AllEligible,
            MaxPublicPerDay = 6,
            MaxPublicPerWeek = 30,
            ReservedRequestSlotsPerDay = 2,
            MaxPublishAhead = TimeSpan.FromDays(3),
            MaxInsertsPerQuotaDay = 80,
        };
}
