using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Outbox;
using HeroesReplay.Core.YouTube.Publication;
using Xunit;

namespace HeroesReplay.Tests.Unit.Retention;

/// <summary>
/// #279: when the pending-bytes gate trips, ordinary recordings past OrdinaryCandidateMaxAge go
/// first. Only a recording the uploader itself would delete as stale goes.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class StaleOrdinaryRecordingsTests : IDisposable
{
    private const int RecordingBytes = 64;
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 2, 29, 57, TimeSpan.Zero);
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(3);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-stale-ordinary-" + Guid.NewGuid().ToString("N")
    );

    public StaleOrdinaryRecordingsTests()
    {
        Directory.CreateDirectory(root);
    }

    [Fact]
    public async Task Clear_RemovesOnlyOrdinaryRecordingsPastTheirAge()
    {
        AppSettings settings = Settings(dryRun: false, privacy: "public");
        string stale = await StageAsync(settings, 101, ReplayMediaPriority.Ordinary, days: 5);
        string fresh = await StageAsync(settings, 102, ReplayMediaPriority.Ordinary, days: 1);
        string request = await StageAsync(settings, 103, ReplayMediaPriority.Requested, days: 5);
        string notable = await StageAsync(settings, 104, ReplayMediaPriority.Notable, days: 5);
        string slotted = await StageAsync(settings, 105, ReplayMediaPriority.Ordinary, days: 5);
        string undecided = await StageAsync(
            settings,
            106,
            ReplayMediaPriority.Ordinary,
            days: 5,
            decision: false
        );
        string launching = await StageAsync(settings, 107, ReplayMediaPriority.Ordinary, days: 5);
        File.WriteAllText(
            PublicationReservation.PathFor(root),
            "reserved|2026-10-09T12:00:00.0000000+00:00|0|replay-105" + Environment.NewLine
        );
        long pendingBefore = Pending(settings);

        RetentionSweep sweep = StaleOrdinaryRecordings.Clear(settings, Now, keepReplayId: 107);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(request));
        Assert.True(File.Exists(notable));
        Assert.True(File.Exists(slotted));
        Assert.True(File.Exists(undecided));
        Assert.True(File.Exists(launching));
        Assert.Equal(1, sweep.DeletedFiles);
        Assert.Equal(RecordingBytes, sweep.FreedBytes);
        Assert.Equal(pendingBefore - RecordingBytes, Pending(settings));
        string warning = Assert.Single(sweep.Warnings);
        Assert.Contains("reason: stale", warning, StringComparison.Ordinal);
        Assert.Contains(stale, warning, StringComparison.Ordinal);
        Assert.Contains("OrdinaryCandidateMaxAge", warning, StringComparison.Ordinal);
    }

    /// <summary>
    /// A dry run never deletes a recording, and a private listing still sends an old ordinary
    /// replay. Neither can prove the recording unpublishable, so it stays.
    /// </summary>
    [Theory]
    [InlineData(true, "public")]
    [InlineData(false, "private")]
    public async Task Clear_KeepsEverythingWhenTheUploaderWouldStillSendIt(
        bool dryRun,
        string privacy
    )
    {
        AppSettings settings = Settings(dryRun, privacy);
        string stale = await StageAsync(settings, 101, ReplayMediaPriority.Ordinary, days: 5);

        RetentionSweep sweep = StaleOrdinaryRecordings.Clear(settings, Now);

        Assert.True(File.Exists(stale));
        Assert.Equal(0, sweep.DeletedFiles);
        Assert.Empty(sweep.Warnings);
    }

    [Fact]
    public async Task Clear_KeepsEverythingWhenRetentionIsOff()
    {
        AppSettings settings = Settings(dryRun: false, privacy: "public");
        settings.Retention = new RetentionSettings { Enabled = false };
        string stale = await StageAsync(settings, 101, ReplayMediaPriority.Ordinary, days: 5);

        RetentionSweep sweep = StaleOrdinaryRecordings.Clear(settings, Now);

        Assert.True(File.Exists(stale));
        Assert.Equal(0, sweep.DeletedFiles);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private AppSettings Settings(bool dryRun, string privacy)
    {
        return new AppSettings
        {
            Location = new LocationSettings { DataDirectory = root },
            Retention = new RetentionSettings { Enabled = true },
            YouTube = new YouTubeSettings
            {
                Enabled = true,
                DryRun = dryRun,
                PrivacyStatus = privacy,
                EntryFileName = "youtube-entry.json",
                EntryFileNameUploaded = "youtube-entry-uploaded.json",
            },
            ReplayMedia = new ReplayMediaPolicySettings { OrdinaryCandidateMaxAge = MaxAge },
        };
    }

    private static long Pending(AppSettings settings) =>
        PendingUploadSize.Bytes(
            settings.ContextsDirectory,
            settings.YouTube.EntryFileName,
            settings.YouTube.EntryFileNameUploaded
        );

    /// <summary>
    /// A finished recording waiting for its insert: the mp4, its entry, and the media decision
    /// the spectator stored when it recorded the game.
    /// </summary>
    private static async Task<string> StageAsync(
        AppSettings settings,
        int replayId,
        ReplayMediaPriority priority,
        int days,
        bool decision = true
    )
    {
        DateTimeOffset played = Now.AddDays(-days);
        string context = Path.Combine(settings.ContextsDirectory, replayId.ToString());
        Directory.CreateDirectory(context);
        string recording = Path.Combine(context, "match.mp4");
        await File.WriteAllBytesAsync(recording, new byte[RecordingBytes]);
        await File.WriteAllTextAsync(
            Path.Combine(context, settings.YouTube.EntryFileName),
            JsonSerializer.Serialize(
                new YouTubeEntry
                {
                    Title = "Dragon Shire - Storm League - " + replayId,
                    ReplayId = replayId,
                    Requested = priority == ReplayMediaPriority.Requested,
                    RecordedAtUtc = played,
                }
            )
        );
        if (!decision)
        {
            return recording;
        }

        UploadAttemptResult saved = await new UploadOutbox(
            MediaPolicyAttemptLog.AttemptsRoot(settings)
        ).SavePolicyAsync(
            "replay-" + replayId,
            replayId,
            played.AddHours(1),
            new UploadAttemptPolicy
            {
                PolicyVersion = "1",
                Record = true,
                RecordingReason = ReplayMediaReason.RecordedOrdinary,
                PublicationCandidate = true,
                PublicationReason =
                    priority == ReplayMediaPriority.Requested
                        ? ReplayMediaReason.EligibleRequested
                        : ReplayMediaReason.EligibleAll,
                Priority = priority.ToString(),
                RecordingMode = ReplayRecordingMode.Selected.ToString(),
                PublicationMode = ReplayPublicationMode.AllEligible.ToString(),
                Score = new UploadAttemptScoreEvidence(),
                PublicationEvaluated = true,
                ReplayId = replayId,
                GameDateUtc = played.UtcDateTime,
            },
            replaceOpen: false,
            CancellationToken.None
        );
        Assert.True(saved.Succeeded, saved.Reason);
        return recording;
    }
}
