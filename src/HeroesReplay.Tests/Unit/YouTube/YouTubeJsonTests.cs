using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Outbox;
using HeroesReplay.Core.YouTube.Playlists;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

/// <summary>The YouTube files keep their bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeJsonTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public void IndentedFiles_KeepTheirBytes()
    {
        YouTubeJsonContext json = YouTubeJsonContext.Indented;
        JsonParity.AssertSame(Indented, json.YouTubeEntry);
        JsonParity.AssertSame(Indented, json.DictionaryStringEntry);
        JsonParity.AssertSame(Indented, json.YouTubePlaylistCache);
        JsonParity.AssertSame(Indented, json.YouTubeUploadsIndex);
        JsonParity.AssertSame(Indented, json.PublicationLedger);
        JsonParity.AssertSame(Indented, json.YouTubeQuotaDay);
    }

    [Fact]
    public void LibraryRecordLines_KeepTheirBytes()
    {
        JsonParity.AssertSame(
            new JsonSerializerOptions(),
            YouTubeJsonContext.Default.YouTubeLibraryVideo
        );
        JsonParity.AssertSame(new JsonSerializerOptions(), YouTubeJsonContext.Default.YouTubeEntry);
    }

    [Fact]
    public void UploadAttemptManifest_KeepsItsBytes()
    {
        var before = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(null, allowIntegerValues: false) },
        };
        JsonParity.AssertSame(before, UploadAttemptManifestCodec.JsonOptions.UploadAttemptManifest);
        JsonParity.AssertSame(before, UploadAttemptManifestCodec.JsonOptions.UploadAttemptPolicy);
    }

    [Fact]
    public void LibraryDryRun_WritesWhatTheAnonymousReportWrote()
    {
        var items = new List<YouTubeLibraryItem>
        {
            new("Valla <Hero> & co", "abc", 7),
            new("Valla <Hero> & co", "def", null),
        };
        DateTimeOffset day = new(2026, 10, 17, 0, 0, 0, TimeSpan.FromHours(-7));
        var anonymous = new
        {
            Simulated = true,
            Recorded = 3,
            Planned = items.Count,
            InsertUnits = 100,
            Playlists = items
                .GroupBy(item => item.PlaylistTitle, StringComparer.Ordinal)
                .Select(group => new { Title = group.Key, Videos = group.Count() }),
            Items = (IReadOnlyList<YouTubeLibraryItem>)items,
            Discovery = new
            {
                WouldList = true,
                UploadsPlaylistKnown = false,
                ListedToEnd = true,
                KnownVideos = 12,
                Unresolved = 2,
                HeroesProfileLookups = 1,
                WouldResolve = items.Select(item => new { item.VideoId, item.ReplayId }),
            },
            Units = new
            {
                QuotaDay = day,
                UploadCalls = 2,
                LibraryUnits = 150,
                LibraryRoom = 850,
            },
        };
        var report = new YouTubeLibraryDryRun(
            true,
            3,
            items.Count,
            100,
            [new YouTubeLibraryDryRunPlaylist("Valla <Hero> & co", 2)],
            items,
            new YouTubeLibraryDryRunDiscovery(
                true,
                false,
                true,
                12,
                2,
                1,
                items
                    .Select(item => new YouTubeLibraryDryRunLookup(item.VideoId, item.ReplayId))
                    .ToList()
            ),
            new YouTubeLibraryDryRunUnits(day, 2, 150, 850)
        );

        Assert.Equal(
            JsonParity.Written(Indented, anonymous),
            JsonSerializer.Serialize(report, YouTubeJsonContext.Indented.YouTubeLibraryDryRun)
        );
    }

    [Fact]
    public void DryRunReceipt_WritesWhatTheAnonymousReceiptWrote()
    {
        DateTimeOffset? publishAt = new DateTimeOffset(2026, 10, 18, 17, 0, 0, TimeSpan.Zero);
        var anonymous = new
        {
            Title = "Valla - Cursed Hollow <&>",
            PrivacyStatus = "private",
            DesiredPrivacyStatus = (string)null,
            PublishAtUtc = publishAt,
            Schedule = "granted",
            ScheduleReason = (string)null,
            SelfDeclaredMadeForKids = (bool?)false,
            CategoryId = "20",
            Bytes = 123_456_789_012L,
            Recording = "match.mp4",
            Simulated = true,
        };
        var receipt = new YouTubeDryRunReceipt(
            "Valla - Cursed Hollow <&>",
            "private",
            null,
            publishAt,
            "granted",
            null,
            false,
            "20",
            123_456_789_012L,
            "match.mp4",
            true
        );

        Assert.Equal(
            JsonParity.Written(Indented, anonymous),
            JsonSerializer.Serialize(receipt, YouTubeJsonContext.Indented.YouTubeDryRunReceipt)
        );
    }
}
