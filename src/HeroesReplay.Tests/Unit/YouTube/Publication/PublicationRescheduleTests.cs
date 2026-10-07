using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.YouTube.Publication;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Publication;

/// <summary>
/// A held slot whose publish time passed before its upload finished (an interrupted or refused
/// send) gets the next valid time instead of going out with a past <c>publishAt</c>.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class PublicationRescheduleTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 16, 17, 17, TimeSpan.Zero);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-reschedule-" + Guid.NewGuid().ToString("N")
    );

    private string Ledger => Path.Combine(root, PublicationReservation.FileName);

    public PublicationRescheduleTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void APassedHeldSlotMovesToTheNextValidTime()
    {
        Assert.Equal(Now, Reserve("replay-1", Now).PublishAtUtc);
        DateTimeOffset later = Now.AddHours(3);

        PublicationReservationResult moved = Reserve("replay-1", later, rescheduleIfBefore: later);

        Assert.True(moved.Allow);
        Assert.Equal(Now, moved.RescheduledFrom);
        Assert.StartsWith("rescheduled-", moved.Reason, StringComparison.Ordinal);
        Assert.True(moved.PublishAtUtc >= later);
        Assert.Equal(moved.PublishAtUtc, PublicationReservation.HeldAt(Ledger, "replay-1"));
        Assert.Single(File.ReadAllLines(Ledger), line => line.Contains("replay-1"));
    }

    [Fact]
    public void AHeldSlotStillAheadKeepsItsTime()
    {
        Reserve("replay-1", Now);
        DateTimeOffset second = Reserve("replay-2", Now, map: "Sky Temple").PublishAtUtc.Value;
        Assert.True(second > Now.AddHours(1));

        PublicationReservationResult kept = Reserve(
            "replay-2",
            Now.AddHours(1),
            map: "Sky Temple",
            rescheduleIfBefore: Now.AddHours(1)
        );

        Assert.Equal("reserved", kept.Reason);
        Assert.Equal(second, kept.PublishAtUtc);
        Assert.Null(kept.RescheduledFrom);
    }

    [Fact]
    public void WithoutTheFlagAPassedSlotIsReturnedAsBefore()
    {
        Reserve("replay-1", Now);

        PublicationReservationResult kept = Reserve("replay-1", Now.AddHours(3));

        Assert.Equal("reserved", kept.Reason);
        Assert.Equal(Now, kept.PublishAtUtc);
    }

    [Fact]
    public void AGrantedSlotKeepsItsGrantPastTheOrdinaryAge()
    {
        Reserve("replay-1", Now, recordedAt: Now.AddHours(-60));
        DateTimeOffset later = Now.AddHours(20);

        PublicationReservationResult moved = Reserve(
            "replay-1",
            later,
            recordedAt: Now.AddHours(-60),
            rescheduleIfBefore: later
        );

        Assert.True(moved.Allow);
        Assert.NotEqual(PublicationReservation.Terminal, moved.Kind);
        Assert.DoesNotContain("terminal|", File.ReadAllText(Ledger), StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoFreeTimeTheSlotStaysAndTheUploadWaits()
    {
        ReplayMediaPolicySettings settings = PublicationSchedule.CanarySettings();
        settings.MaxPublishAhead = TimeSpan.FromHours(1);
        Reserve("replay-1", Now, settings: settings);
        DateTimeOffset second = Now.AddHours(2).AddMinutes(59);
        Assert.Equal(
            second,
            Reserve(
                "replay-2",
                second,
                map: "Sky Temple",
                hero: "Zeratul",
                settings: settings
            ).PublishAtUtc
        );
        DateTimeOffset later = Now.AddHours(3);

        PublicationReservationResult refused = Reserve(
            "replay-1",
            later,
            settings: settings,
            rescheduleIfBefore: later
        );

        Assert.False(refused.Allow);
        Assert.Equal(PublicationSchedule.PublicationFull, refused.Reason);
        Assert.Equal(Now, PublicationReservation.HeldAt(Ledger, "replay-1"));
    }

    [Fact]
    public void ARequestIsRescheduledToNow()
    {
        Reserve("replay-7", Now, requested: true);
        DateTimeOffset later = Now.AddHours(5);

        PublicationReservationResult moved = Reserve(
            "replay-7",
            later,
            requested: true,
            rescheduleIfBefore: later
        );

        Assert.Equal(later, moved.PublishAtUtc);
        Assert.Equal(Now, moved.RescheduledFrom);
    }

    [Fact]
    public void HeldAt_IsNullWithoutASlot()
    {
        Assert.Null(PublicationReservation.HeldAt(Ledger, "replay-1"));
        Assert.Null(PublicationReservation.HeldAt(null, "replay-1"));
    }

    private PublicationReservationResult Reserve(
        string workKey,
        DateTimeOffset now,
        bool requested = false,
        DateTimeOffset? recordedAt = null,
        string map = "Braxis Holdout",
        string hero = "Li-Ming",
        ReplayMediaPolicySettings settings = null,
        DateTimeOffset? rescheduleIfBefore = null
    ) =>
        PublicationReservation.TryReserve(
            Ledger,
            true,
            0,
            now,
            null,
            new List<DateTimeOffset>(),
            0,
            requested,
            recordedAt ?? Now.AddHours(-1),
            map,
            null,
            null,
            hero,
            null,
            null,
            workKey,
            settings,
            rescheduleIfBefore: rescheduleIfBefore
        );
}
