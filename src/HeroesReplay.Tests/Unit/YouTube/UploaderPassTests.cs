using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Outbox;
using HeroesReplay.Core.YouTube.Playlists;
using HeroesReplay.Core.YouTube.Publication;
using HeroesReplay.Core.YouTube.Search;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

/// <summary>
/// What the uploader counts as healthy work for <c>services status</c>, and how often it logs a
/// recording that waits. None of these passes reaches YouTube.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class UploaderPassTests : IDisposable
{
    private const string Session =
        "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&upload_id=abc";

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-uploader-pass-" + Guid.NewGuid().ToString("N")
    );

    private readonly ListLogger log = new();
    private int work;

    public UploaderPassTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task APassWithNothingPending_IsWork()
    {
        YouTubeUploader uploader = Uploader(Settings(live: true));

        await uploader.RunUploadPassAsync();

        Assert.Equal(1, work);
    }

    [Fact]
    public async Task ADryRunReceipt_IsWork()
    {
        AppSettings settings = Settings(live: false);
        string recording = await StageAsync(settings, 65550001, "Alterac Pass");
        YouTubeUploader uploader = Uploader(settings);

        UploadOutcome outcome = await uploader.ProcessRecordingAsync(recording);

        Assert.Equal(UploadOutcome.Uploaded, outcome);
        Assert.True(work >= 1);
    }

    /// <summary>
    /// #317: the youtube role sweeps a dry-run recording once its plan is written and it is older
    /// than Retention:DryRunRecordingMaxAge. A young one stays; the plan and entry stay.
    /// </summary>
    [Fact]
    public async Task ADryRunPass_RemovesAPlannedRecordingPastTheMaxAge()
    {
        AppSettings settings = Settings(live: false);
        settings.Retention = new RetentionSettings { DryRunRecordingMaxAge = TimeSpan.FromDays(2) };
        string recording = await StageAsync(settings, 65581722, "Braxis Holdout");
        string context = Path.GetDirectoryName(recording);
        YouTubeUploader uploader = Uploader(settings);

        Assert.Equal(UploadOutcome.Uploaded, await uploader.ProcessRecordingAsync(recording));
        Assert.True(File.Exists(recording));
        File.SetLastWriteTimeUtc(recording, DateTime.UtcNow.AddDays(-3));
        await uploader.RunUploadPassAsync();

        Assert.False(File.Exists(recording));
        Assert.True(File.Exists(Path.Combine(context, DryRunRecordings.PlanFileName)));
        Assert.True(File.Exists(Path.Combine(context, "youtube-entry.json")));
        Assert.Single(log.Lines, line => line.StartsWith("Removed 1 dry-run recording(s)"));
    }

    /// <summary>
    /// Production is unchanged: a live pass never removes a recording, even one an earlier dry
    /// run planned, while the age is set.
    /// </summary>
    [Fact]
    public async Task ALivePass_KeepsARecordingAnEarlierDryRunPlanned()
    {
        AppSettings settings = Settings(live: true);
        settings.Retention = new RetentionSettings { DryRunRecordingMaxAge = TimeSpan.FromDays(2) };
        string recording = await StageAsync(settings, 65733007, "Alterac Pass");
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(recording), DryRunRecordings.PlanFileName),
            "{}"
        );
        File.SetLastWriteTimeUtc(recording, DateTime.UtcNow.AddDays(-3));
        // The insert cap holds the send, so the pass never reaches YouTube.
        PublicationLedgerStore.Save(
            root,
            new PublicationLedger
            {
                InsertsThisQuotaDay = settings.ReplayMedia.MaxInsertsPerQuotaDay,
                QuotaDay = PublicationSchedule.QuotaDayStart(DateTimeOffset.UtcNow),
            }
        );
        YouTubeUploader uploader = Uploader(settings);

        await uploader.RunUploadPassAsync();

        Assert.True(File.Exists(recording));
        Assert.DoesNotContain(log.Lines, line => line.Contains("dry-run recording"));
    }

    [Fact]
    public async Task APassWithAnUnreadableEntry_IsNotWork()
    {
        AppSettings settings = Settings(live: false);
        string recording = await StageAsync(settings, 65550002, "Sky Temple");
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(recording), "youtube-entry.json"),
            "null"
        );
        YouTubeUploader uploader = Uploader(settings);

        await uploader.RunUploadPassAsync();

        Assert.Equal(0, work);
    }

    [Fact]
    public async Task TheInsertCap_HoldsEveryRecording_LogsOnce_AndIsStillWork()
    {
        AppSettings settings = Settings(live: true);
        string[] recordings =
        {
            await StageAsync(settings, 65733006, "Alterac Pass"),
            await StageAsync(settings, 65749071, "Sky Temple"),
            await StageAsync(settings, 65783070, "Cursed Hollow"),
        };
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PublicationLedgerStore.Save(
            root,
            new PublicationLedger
            {
                InsertsThisQuotaDay = settings.ReplayMedia.MaxInsertsPerQuotaDay,
                QuotaDay = PublicationSchedule.QuotaDayStart(now),
            }
        );
        YouTubeUploader uploader = Uploader(settings);

        await uploader.RunUploadPassAsync();
        await uploader.RunUploadPassAsync();

        Assert.Equal(2, work);
        Assert.Single(log.Lines, line => line.Contains("daily insert cap is reached"));
        Assert.DoesNotContain(log.Lines, line => line.Contains("no publish time"));
        Assert.DoesNotContain(log.Lines, line => line.Contains("(quota)"));
        Assert.DoesNotContain(log.Lines, line => line.StartsWith("Uploading"));
        Assert.Single(log.Lines, line => line.StartsWith("YouTube publication health"));
        Assert.Contains(
            "limit insert-cap",
            log.Lines.Single(line => line.StartsWith("YouTube publication health"))
        );
        Assert.All(recordings, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task AFullSchedule_IsNamedPublicationFull_LoggedOncePerRecording_WithoutWatchingTheFile()
    {
        AppSettings settings = Settings(live: true);
        settings.ReplayMedia.MaxPublishAhead = TimeSpan.FromMinutes(30);
        settings.YouTube.ReadyStableReads = 50;
        settings.YouTube.ReadyPollMilliseconds = 1000;
        string recording = await StageAsync(settings, 65733010, "Alterac Pass");
        File.SetLastWriteTimeUtc(recording, DateTime.UtcNow.AddMinutes(-10));
        File.WriteAllText(
            PublicationReservation.PathFor(root),
            "reserved|"
                + DateTimeOffset.UtcNow.ToString("o")
                + "|0|replay-1|Sky%20Temple|Master||"
                + Environment.NewLine
        );
        YouTubeUploader uploader = Uploader(settings);

        var timer = System.Diagnostics.Stopwatch.StartNew();
        await uploader.RunUploadPassAsync();
        await uploader.RunUploadPassAsync();
        timer.Stop();

        Assert.Single(
            log.Lines,
            line => line.StartsWith("Upload of") && line.Contains("publication-full")
        );
        Assert.Contains(
            "Waiting: publication-full 1",
            log.Lines.Single(line => line.StartsWith("YouTube publication health"))
        );
        Assert.DoesNotContain(log.Lines, line => line.StartsWith("Recording ready"));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(20), timer.Elapsed.ToString());
        Assert.Equal(2, work);
    }

    [Fact]
    public async Task AnInterruptedUploadWithItsRetriesUsed_IsParkedOnce_AndIsNotWork()
    {
        AppSettings settings = Settings(live: true);
        settings.YouTube.InterruptedUploadRetries = 2;
        string recording = await StageAsync(settings, 65719257, "Sky Temple");
        string attemptId = await StageInterruptedAsync(recording, 65719257, Session);
        var retries = new InterruptedUploadRetries(root);
        retries.Add(attemptId, DateTimeOffset.UtcNow);
        retries.Add(attemptId, DateTimeOffset.UtcNow);
        YouTubeUploader uploader = Uploader(settings);

        await uploader.RunUploadPassAsync();
        await uploader.RunUploadPassAsync();

        Assert.Equal(0, work);
        Assert.Single(log.Lines, line => line.Contains("automatic retries are used"));
        Assert.Equal(2, new InterruptedUploadRetries(root).Count(attemptId));
    }

    [Fact]
    public async Task AnInterruptedUploadWithNoSession_ForAReplayOnTheChannel_IsNotSentAgain()
    {
        AppSettings settings = Settings(live: true);
        string recording = await StageAsync(settings, 65719258, "Sky Temple");
        string attemptId = await StageInterruptedAsync(recording, 65719258, session: null);
        YouTubeReplayCatalog.Remember(YouTubeReplayCatalog.PathFor(root), 65719258);
        YouTubeUploader uploader = Uploader(settings);

        await uploader.RunUploadPassAsync();

        Assert.Single(log.Lines, line => line.Contains("is already on the channel"));
        Assert.Equal(0, new InterruptedUploadRetries(root).Count(attemptId));
        UploadAttemptResult loaded = await new UploadOutbox(
            MediaPolicyAttemptLog.AttemptsRoot(settings)
        ).LoadAsync(attemptId, CancellationToken.None);
        Assert.Equal(UploadAttemptState.AmbiguousUpload, loaded.Manifest.State);
    }

    [Fact]
    public async Task AnInterruptedUploadThatCannotSignIn_KeepsItsStateAndItsRetries()
    {
        AppSettings settings = Settings(live: true);
        string recording = await StageAsync(settings, 65719259, "Sky Temple");
        string attemptId = await StageInterruptedAsync(recording, 65719259, session: null);
        YouTubeUploader uploader = Uploader(settings);

        await uploader.RunUploadPassAsync();

        Assert.Equal(0, work);
        Assert.Equal(0, new InterruptedUploadRetries(root).Count(attemptId));
        UploadAttemptResult loaded = await new UploadOutbox(
            MediaPolicyAttemptLog.AttemptsRoot(settings)
        ).LoadAsync(attemptId, CancellationToken.None);
        Assert.Equal(UploadAttemptState.AmbiguousUpload, loaded.Manifest.State);
    }

    [Theory]
    [InlineData(null, "ran", 1)]
    [InlineData("not-due", "skipped", 1)]
    [InlineData("units-spent", "skipped", 1)]
    [InlineData("no-consent", "skipped", 0)]
    public async Task ALibraryPassIsWork_UnlessItCannotRunForWantOfConsent(
        string skipCode,
        string skipped,
        int expected
    )
    {
        var library = new FakeLibrary
        {
            Pass = new YouTubeLibraryPass
            {
                SkipCode = skipCode,
                Skipped = skipCode == null ? null : skipped,
            },
        };
        YouTubeUploader uploader = Uploader(Settings(live: true), library);

        await uploader.FileLibraryAsync(startup: true, CancellationToken.None);

        Assert.Equal(expected, work);
        Assert.True(library.Startup);
    }

    [Fact]
    public void Health_TheInsertCapIsNamedAsThisAppsCap_AndStillQuotaBlocked()
    {
        YouTubeUploaderConcern concern = YouTubeUploaderHealth.Evaluate(
            new YouTubeUploaderHealthInput
            {
                Live = true,
                PublicListing = true,
                Pending = 17,
                InsertCapped = true,
                InsertCap = 20,
                Now = DateTimeOffset.UtcNow,
                Tally = new PublicationTallyReport(),
            }
        );

        Assert.Equal(YouTubeUploaderHealth.QuotaBlockedCode, concern.Code);
        Assert.Contains("MaxInsertsPerQuotaDay 20", concern.Cause, StringComparison.Ordinal);
    }

    [Fact]
    public void Health_NotPublishingNamesAMissingLibraryConsent()
    {
        YouTubeUploaderConcern concern = YouTubeUploaderHealth.Evaluate(
            new YouTubeUploaderHealthInput
            {
                Live = true,
                PublicListing = true,
                Now = DateTimeOffset.UtcNow,
                Tally = new PublicationTallyReport { Due = 14, StuckPrivate = 14 },
                LibraryProblem = "no stored consent for UC-library",
            }
        );

        Assert.Equal(YouTubeUploaderHealth.NotPublishingCode, concern.Code);
        Assert.Contains("no stored consent", concern.Cause, StringComparison.Ordinal);
    }

    private YouTubeUploader Uploader(AppSettings settings, IYouTubeLibrary library = null) =>
        new(log, settings, new CancellationTokenSource(), library)
        {
            ReplaySessionFilePath = Path.Combine(root, "replay-sessions.txt"),
            RecordWork = () => Interlocked.Increment(ref work),
        };

    private AppSettings Settings(bool live)
    {
        ReplayMediaPolicySettings media = PublicationSchedule.CanarySettings();
        return new AppSettings
        {
            YouTube = new YouTubeSettings
            {
                DryRun = !live,
                Enabled = true,
                PrivacyStatus = "public",
                EntryFileName = "youtube-entry.json",
                EntryFileNameUploaded = "youtube-entry-uploaded.json",
                ReadyStableReads = 1,
                ReadyPollMilliseconds = 20,
                ChannelId = "UC-test",
            },
            Location = new LocationSettings { DataDirectory = root },
            ReplayMedia = media,
        };
    }

    private async Task<string> StageAsync(AppSettings settings, int replayId, string map)
    {
        string context = Path.Combine(settings.ContextsDirectory, replayId.ToString());
        Directory.CreateDirectory(context);
        string recording = Path.Combine(context, "match.mp4");
        await File.WriteAllBytesAsync(recording, new byte[] { 0, 0, 0, 24, 102, 116, 121, 112 });
        await File.WriteAllTextAsync(
            Path.Combine(context, "youtube-entry.json"),
            JsonSerializer.Serialize(
                new YouTubeEntry
                {
                    Title = map + " - Storm League - Diamond - " + replayId,
                    ReplayId = replayId,
                    Map = map,
                    Rank = "Diamond",
                    PrivacyStatus = "public",
                    CategoryId = "20",
                }
            )
        );

        DateTime now = DateTime.UtcNow;
        ReplayMediaDecision decision = ReplayMediaPolicy.Evaluate(
            new ReplayMediaPolicyInput
            {
                ReplayId = replayId,
                GameDateUtc = now,
                GameVersion = "2.57.0.98304",
                Map = map,
                GameMode = "Storm League",
                Rank = "Diamond",
                Completion = new ReplayMediaCompletion { IsVerifiedComplete = true },
                Media = new ReplayMediaFinalization { IsFinalized = true, IsCorrelated = true },
            },
            settings.ReplayMedia,
            now
        );
        UploadAttemptResult saved = await new UploadOutbox(
            MediaPolicyAttemptLog.AttemptsRoot(settings)
        ).SavePolicyAsync(
            "replay-" + replayId,
            replayId,
            new DateTimeOffset(now),
            MediaPolicyManifest.FromDecision(decision, publicationEvaluated: true),
            replaceOpen: false,
            CancellationToken.None
        );
        Assert.True(saved.Succeeded, saved.Reason);
        return recording;
    }

    /// <summary>An attempt whose send was cut off: what the release restart left on 2026-10-07.</summary>
    private async Task<string> StageInterruptedAsync(string recording, int replayId, string session)
    {
        var outbox = new UploadOutbox(
            Path.Combine(root, MediaPolicyAttemptLog.AttemptsDirectoryName)
        );
        string attemptId = "replay-" + replayId + "-20261007161717";
        DateTimeOffset at = DateTimeOffset.UtcNow.AddMinutes(-5);
        long size = new FileInfo(recording).Length;
        SavedDispatch sent = await outbox.SaveDispatchAsync(
            attemptId,
            replayId,
            recording,
            size,
            "len-" + size,
            youtubeEnabled: true,
            dryRun: false,
            at,
            CancellationToken.None
        );
        Assert.True(sent.MaySend, sent.Result.Reason);
        if (session != null)
        {
            Assert.True(
                (
                    await outbox.NoteSessionAsync(attemptId, session, at, CancellationToken.None)
                ).Succeeded
            );
        }

        Assert.True(
            (await outbox.MarkAmbiguousAsync(attemptId, at, CancellationToken.None)).Succeeded
        );
        return attemptId;
    }

    private sealed class FakeLibrary : IYouTubeLibrary
    {
        public YouTubeLibraryPass Pass { get; init; }
        public bool Startup { get; private set; }

        public Task<YouTubeLibraryPass> RunOnceAsync(
            bool force,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("The uploader runs the background pass.");

        public Task<YouTubeLibraryPass> RunInBackgroundAsync(
            bool startup,
            CancellationToken cancellationToken
        )
        {
            Startup = startup;
            return Task.FromResult(Pass);
        }
    }

    private sealed class ListLogger : ILogger<YouTubeUploader>
    {
        private readonly ConcurrentQueue<string> lines = new();

        public string[] Lines => lines.ToArray();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => lines.Enqueue(formatter(state, exception));
    }
}
