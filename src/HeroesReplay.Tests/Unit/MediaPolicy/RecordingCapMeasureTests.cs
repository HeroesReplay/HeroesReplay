using System;
using System.Globalization;
using System.IO;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Publication;
using Xunit;

namespace HeroesReplay.Tests.Unit.MediaPolicy;

/// <summary>
/// #370: what the recording cap reads from <c>Data</c>. An upload waiting only for its publish
/// time is a slot, not a recording. A recording whose failed send kept its slot is counted once.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class RecordingCapMeasureTests : IDisposable
{
    private const string Entry = "youtube-entry.json";
    private const string Uploaded = "youtube-entry-uploaded.json";
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 16, 5, 0, TimeSpan.Zero);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-recording-cap-" + Guid.NewGuid().ToString("N")
    );

    public RecordingCapMeasureTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Measure_CountsEachRecordingOrSlotOnce()
    {
        AppSettings settings = Settings();
        // Waits for upload and needs a slot.
        Context(settings, 1, mp4: true, "{\"ReplayId\":1}");
        // Its send failed after it reserved a slot ahead: that slot counts, the recording does not.
        Context(settings, 2, mp4: true, "{\"ReplayId\":2}");
        // Its slot passed before it was sent, so it needs a new one.
        Context(settings, 3, mp4: true, "{\"ReplayId\":3}");
        // Uploaded, the mp4 is gone, and it waits only for its publish time.
        Context(
            settings,
            4,
            mp4: false,
            "{\"ReplayId\":4,\"VideoId\":\"abc\",\"PublishAtUtc\":\"2026-10-10T12:52:37+00:00\"}"
        );
        File.WriteAllLines(
            PublicationReservation.PathFor(root),
            new[]
            {
                "reserved|2026-10-09T10:52:37.0000000+00:00|0|replay-2",
                "reserved|2026-10-08T06:52:37.0000000+00:00|0|replay-3",
                "reserved|2026-10-10T12:52:37.0000000+00:00|0|replay-4",
            }
        );
        var decision = new ReplayMediaDecision
        {
            Priority = ReplayMediaPriority.Ordinary,
            CandidateExpiresAtUtc = new DateTime(2026, 10, 11, 10, 0, 0, DateTimeKind.Utc),
        };

        RecordingCapInput input = RecordingCap.Measure(settings, decision, Now);

        Assert.Equal(2, input.PendingUploads);
        Assert.Equal(2, input.ScheduledAhead);
        Assert.Equal(ReplayMediaPriority.Ordinary, input.Priority);
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 10, 0, 0, TimeSpan.Zero), input.ExpiresAtUtc);
        Assert.Equal(Now, input.Now);
        Assert.True(input.Live);
        Assert.True(input.PublicListing);
    }

    private AppSettings Settings() =>
        new()
        {
            Location = new LocationSettings { DataDirectory = root },
            YouTube = new YouTubeSettings
            {
                Enabled = true,
                DryRun = false,
                PrivacyStatus = "public",
                EntryFileName = Entry,
                EntryFileNameUploaded = Uploaded,
            },
            ReplayMedia = new ReplayMediaPolicySettings { MaxInsertsPerQuotaDay = 20 },
        };

    private static void Context(AppSettings settings, int replayId, bool mp4, string entry)
    {
        string directory = Directory
            .CreateDirectory(
                Path.Combine(
                    settings.ContextsDirectory,
                    replayId.ToString(CultureInfo.InvariantCulture)
                )
            )
            .FullName;
        if (mp4)
        {
            File.WriteAllBytes(Path.Combine(directory, "match.mp4"), new byte[64]);
        }

        File.WriteAllText(Path.Combine(directory, Entry), entry);
    }
}
