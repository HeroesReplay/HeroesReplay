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

        // The stream PC since 2026-10-07: 12 a day less 2 is 10, at 84/7 = 12 a week, over 3 days.
        Assert.Equal(30, RecordingCap.PublicationCapacity(StreamPcSettings(), 20));

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

    /// <summary>
    /// #370, the stream PC on 2026-10-08: 10 recordings wait for upload and 29 uploads wait only
    /// for their publish time, against a window of 30 (10 a day for 3 days). A replay played 6 h
    /// ago is 11th in line: it waits about a day for a slot and is still sent with more than a
    /// day of its 3-day age to spare, so it is recorded.
    /// </summary>
    [Fact]
    public void FullWindow_RecordsAReplayThatIsStillSentBeforeItExpires()
    {
        RecordingCapDecision decision = RecordingCap.Decide(
            StreamPc(pending: 10, ahead: 29, playedAgo: TimeSpan.FromHours(6)),
            StreamPcSettings()
        );

        Assert.True(decision.Allow);
        Assert.Equal(RecordingCap.Queued, decision.Reason);
        Assert.Equal(30, decision.Capacity);
        Assert.Equal(39, decision.InFlight);
        Assert.Equal(TimeSpan.FromDays(1), decision.Wait);
        Assert.Contains(
            "10 recording(s) wait for upload and 29 upload(s) wait for their publish time",
            decision.Summary,
            StringComparison.Ordinal
        );
        Assert.Contains("opens in about 24 h", decision.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same queue, but the game was played 2 days ago: its slot would come a day before its
    /// 3-day age runs out, inside the one-day slack, so the uploader could well delete it as
    /// stale. It is not recorded.
    /// </summary>
    [Fact]
    public void FullWindow_SkipsAReplayThatWouldExpireWhileItWaits()
    {
        RecordingCapDecision decision = RecordingCap.Decide(
            StreamPc(pending: 10, ahead: 29, playedAgo: TimeSpan.FromDays(2)),
            StreamPcSettings()
        );

        Assert.False(decision.Allow);
        Assert.Equal(RecordingCap.PublicationFull, decision.Reason);
        Assert.Contains("would not be sent", decision.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each recording already waiting is a slot the window must open first, at 10 a day. Six
    /// hours after the game 2.75 days are left: 16 ahead of it is 1.7 days plus the slack, 17 is
    /// 1.8 days plus the slack.
    /// </summary>
    [Theory]
    [InlineData(16, true, RecordingCap.Queued)]
    [InlineData(17, false, RecordingCap.PublicationFull)]
    public void FullWindow_RecordsWhileTheQueueDrainsBeforeTheReplayExpires(
        int pending,
        bool allow,
        string reason
    )
    {
        RecordingCapDecision decision = RecordingCap.Decide(
            StreamPc(pending, ahead: 30, playedAgo: TimeSpan.FromHours(6)),
            StreamPcSettings()
        );

        Assert.Equal(allow, decision.Allow);
        Assert.Equal(reason, decision.Reason);
    }

    /// <summary>
    /// A free slot in the window sends the recording at once, so even a replay close to its age
    /// is recorded, as before #370.
    /// </summary>
    [Fact]
    public void FreeSlot_RecordsWhateverTheReplaysAge()
    {
        RecordingCapDecision decision = RecordingCap.Decide(
            StreamPc(pending: 2, ahead: 27, playedAgo: TimeSpan.FromDays(2.9)),
            StreamPcSettings()
        );

        Assert.True(decision.Allow);
        Assert.Equal(RecordingCap.Room, decision.Reason);
        Assert.Equal(TimeSpan.Zero, decision.Wait);
    }

    /// <summary>One day of upload calls waiting still stops a recording, however fresh.</summary>
    [Fact]
    public void UploadBacklog_StillStopsAFreshReplay()
    {
        RecordingCapDecision decision = RecordingCap.Decide(
            StreamPc(pending: 20, ahead: 0, playedAgo: TimeSpan.FromMinutes(30)),
            StreamPcSettings()
        );

        Assert.False(decision.Allow);
        Assert.Equal(RecordingCap.UploadBacklog, decision.Reason);
        Assert.Equal(20, decision.Capacity);
    }

    /// <summary>A replay with no game time cannot be shown to be sent in time, so it waits for room.</summary>
    [Fact]
    public void FullWindow_WithoutAGameTimeIsNotRecorded()
    {
        RecordingCapDecision decision = RecordingCap.Decide(
            new RecordingCapInput
            {
                Priority = ReplayMediaPriority.Ordinary,
                PendingUploads = 1,
                ScheduledAhead = 30,
                Live = true,
                PublicListing = true,
                UploadsPerDay = 20,
                Now = Now,
            },
            StreamPcSettings()
        );

        Assert.False(decision.Allow);
        Assert.Equal(RecordingCap.PublicationFull, decision.Reason);
        Assert.Contains("game time is unknown", decision.Summary, StringComparison.Ordinal);
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 8, 16, 5, 0, TimeSpan.Zero);

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

    /// <summary>An ordinary replay on the stream PC: 20 inserts a day, expiring 3 days after its game.</summary>
    private static RecordingCapInput StreamPc(int pending, int ahead, TimeSpan playedAgo) =>
        new()
        {
            Priority = ReplayMediaPriority.Ordinary,
            PendingUploads = pending,
            ScheduledAhead = ahead,
            Live = true,
            PublicListing = true,
            UploadsPerDay = 20,
            ExpiresAtUtc = Now - playedAgo + TimeSpan.FromDays(3),
            Now = Now,
        };

    /// <summary><c>appsettings.prod.json</c> since 2026-10-07: 12 a day, 84 a week, 2 for requests.</summary>
    private static ReplayMediaPolicySettings StreamPcSettings()
    {
        ReplayMediaPolicySettings settings = Production();
        settings.MaxPublicPerDay = 12;
        settings.MaxPublicPerWeek = 84;
        settings.MaxInsertsPerQuotaDay = 20;
        settings.OrdinaryCandidateMaxAge = TimeSpan.FromDays(3);
        return settings;
    }

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
