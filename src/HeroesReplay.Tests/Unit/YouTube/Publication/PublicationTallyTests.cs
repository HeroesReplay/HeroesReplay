using System;
using System.IO;
using HeroesReplay.Core.YouTube.Playlists;
using HeroesReplay.Core.YouTube.Publication;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Publication;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PublicationTallyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Grace = TimeSpan.FromHours(2);

    /// <summary>
    /// #250: production uploaded 6 a day, each private with a publishAt days ahead, and
    /// reported published-week 0 and stuck-private +6 a day. A scheduled upload is not stuck,
    /// and a video the library pass saw go public is published.
    /// </summary>
    [Fact]
    public void Count_ScheduledUploadsAreNotStuckAndConfirmedOnesArePublished()
    {
        YouTubeLibraryVideo[] videos =
        {
            Video("pub-1", "public", Now.AddHours(-3)),
            Video("pub-2", "public", Now.AddHours(-20)),
            Video("pub-3", "public", Now.AddDays(-3)),
            Video("pub-old", "public", Now.AddDays(-9)),
            Video("ahead-1", "private", Now.AddDays(1)),
            Video("ahead-2", "private", Now.AddDays(2)),
            Video("due-now", "private", Now.AddMinutes(-30)),
            Video("stuck", "private", Now.AddHours(-5)),
            Video("ancient", "private", Now.AddDays(-40)),
            Video("test-private", "private", null),
        };

        PublicationTallyReport tally = PublicationTally.Count(videos, Now, Grace);

        Assert.Equal(2, tally.PublishedDay);
        Assert.Equal(3, tally.PublishedWeek);
        Assert.Equal(3, tally.Scheduled);
        Assert.Equal(2, tally.Due);
        Assert.Equal(1, tally.StuckPrivate);
        Assert.Equal(new[] { "stuck" }, tally.StuckVideoIds);
        Assert.Equal(Now.AddHours(-3), tally.LastPublicAt);
    }

    [Fact]
    public void Count_BackfilledPublicVideoUsesItsUploadTime()
    {
        var video = new YouTubeLibraryVideo
        {
            VideoId = "old",
            PrivacyStatus = "public",
            UploadedAt = Now.AddHours(-1),
        };

        PublicationTallyReport tally = PublicationTally.Count(new[] { video }, Now, Grace);

        Assert.Equal(1, tally.PublishedDay);
        Assert.Equal(Now.AddHours(-1), tally.LastPublicAt);
    }

    [Fact]
    public void ConfirmGrace_IsTwoLibraryPassesAndAtLeastTwoHours()
    {
        Assert.Equal(TimeSpan.FromHours(2), PublicationTally.ConfirmGrace(TimeSpan.FromHours(1)));
        Assert.Equal(TimeSpan.FromHours(6), PublicationTally.ConfirmGrace(TimeSpan.FromHours(3)));
        Assert.Equal(TimeSpan.FromHours(2), PublicationTally.ConfirmGrace(TimeSpan.Zero));
    }

    [Fact]
    public void Reservations_CountAheadAndInTheLastDay()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-tally-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            string path = PublicationReservation.PathFor(root);
            File.WriteAllText(
                path,
                string.Join(
                    "\n",
                    Line(Now.AddHours(-30), "replay-1"),
                    Line(Now.AddHours(-3), "replay-2"),
                    Line(Now.AddHours(5), "replay-3"),
                    Line(Now.AddDays(2), "replay-4")
                )
            );

            Assert.Equal(2, PublicationReservation.CountAfter(path, Now));
            Assert.Equal(1, PublicationReservation.CountIn(path, Now, TimeSpan.FromHours(24)));
            Assert.Equal(
                0,
                PublicationReservation.CountAfter(PublicationReservation.PathFor(null), Now)
            );
            Assert.EndsWith(
                "publication-reservations-dry-run.txt",
                PublicationReservation.PathFor(root, dryRun: true),
                StringComparison.Ordinal
            );
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string Line(DateTimeOffset at, string key) =>
        "reserved|" + at.ToString("o") + "|0|" + key;

    private static YouTubeLibraryVideo Video(
        string id,
        string privacy,
        DateTimeOffset? publishAt
    ) =>
        new()
        {
            VideoId = id,
            PrivacyStatus = privacy,
            PublishAt = publishAt,
            UploadedAt = Now.AddDays(-10),
        };
}
