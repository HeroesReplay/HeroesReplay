using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.Core.Services.YouTube;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PublicationReservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryReserve_PersistsOneSlotAndStopsASecondOne()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-slots-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "publication-reservations.txt");
        try
        {
            PublicationReservationResult first = Reserve(
                path,
                Now,
                false,
                Now.AddHours(-1),
                "replay-1"
            );
            string saved = File.ReadAllText(path);
            string reread = File.ReadAllText(path);
            PublicationReservationResult same = Reserve(
                path,
                Now.AddMinutes(1),
                false,
                Now.AddHours(-1),
                "replay-1"
            );
            PublicationReservationResult second = Reserve(
                path,
                Now.AddMinutes(30),
                false,
                Now.AddHours(-1),
                "replay-2"
            );

            Assert.True(first.Allow);
            Assert.Equal(PublicationReservation.Granted, first.Kind);
            Assert.Contains("reserved|", saved, StringComparison.Ordinal);
            Assert.Contains("replay-1", saved, StringComparison.Ordinal);
            Assert.Equal(saved, reread);
            Assert.True(same.Allow);
            Assert.Equal(1, CountLines(path, "replay-1"));
            Assert.False(second.Allow);
            Assert.Equal("interval", second.Reason);
            Assert.Equal(PublicationReservation.Refused, second.Kind);
            Assert.Equal(0, CountLines(path, "replay-2"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void TryReserve_MarksStaleOrdinaryWorkTerminal()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-stale-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "publication-reservations.txt");
        try
        {
            PublicationReservationResult stale = Reserve(
                path,
                Now,
                false,
                Now.AddHours(-80),
                "replay-9"
            );
            string saved = File.ReadAllText(path);
            PublicationReservationResult again = Reserve(
                path,
                Now.AddHours(1),
                false,
                Now.AddHours(-80),
                "replay-9"
            );

            Assert.Equal(PublicationReservation.Terminal, stale.Kind);
            Assert.Equal("stale", stale.Reason);
            Assert.False(stale.Allow);
            Assert.Contains("terminal|replay-9", saved, StringComparison.Ordinal);
            Assert.DoesNotContain("reserved|", saved, StringComparison.Ordinal);
            Assert.Equal(PublicationReservation.Terminal, again.Kind);
            Assert.Equal(1, CountLines(path, "terminal|replay-9"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void WriteStatus_ListsCandidatesReservedCapacityAndTheNextSlot()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-status-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "publication-status.txt");
        try
        {
            PublicationHealthReport report = PublicationHealth.Summarize(
                2,
                1,
                0,
                false,
                "1",
                1,
                4,
                1
            );
            DateTimeOffset next = Now.AddHours(2);
            PublicationHealth.WriteStatus(
                path,
                report,
                new[] { "match-a.mp4", "match-b.mp4" },
                PublicationSchedule.ReservedRequestSlotsPerDay,
                next
            );
            string text = File.ReadAllText(path);
            string reread = File.ReadAllText(path);

            Assert.Equal(2, report.Pending);
            Assert.Contains("candidates=match-a.mp4,match-b.mp4", text, StringComparison.Ordinal);
            Assert.Contains(
                "reserved=" + PublicationSchedule.ReservedRequestSlotsPerDay,
                text,
                StringComparison.Ordinal
            );
            Assert.Contains("next=" + next.ToString("o"), text, StringComparison.Ordinal);
            Assert.Equal(text, reread);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static PublicationReservationResult Reserve(
        string path,
        DateTimeOffset now,
        bool requested,
        DateTimeOffset? recordedAtUtc,
        string workKey
    )
    {
        return PublicationReservation.TryReserve(
            path,
            true,
            0,
            now,
            null,
            new List<DateTimeOffset>(),
            0,
            requested,
            recordedAtUtc,
            "Braxis Holdout",
            null,
            null,
            "Li-Ming",
            null,
            null,
            workKey
        );
    }

    private static int CountLines(string path, string fragment)
    {
        int count = 0;
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Contains(fragment, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }
}
