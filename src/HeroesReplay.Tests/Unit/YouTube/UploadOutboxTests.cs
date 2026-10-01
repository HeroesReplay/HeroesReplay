using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Services.YouTube.Outbox;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class UploadOutboxTests
{
    private static readonly DateTimeOffset Stamp = new DateTimeOffset(
        2026,
        9,
        29,
        3,
        4,
        5,
        TimeSpan.Zero
    );

    [Fact]
    public async Task RepeatedReplayIds_KeepSeparateMediaAndVideoIds()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        const int replayId = 100;
        string firstPath = Path.Combine(temp.Root, "attempt-a", "final.mp4");
        string secondPath = Path.Combine(temp.Root, "attempt-b", "final.mp4");

        await ReachUploadingAsync(outbox, "attempt-a", replayId, firstPath, "hash-a");
        await ReachUploadingAsync(outbox, "attempt-b", replayId, secondPath, "hash-b");
        await Require(
            outbox.CompleteAsync(
                "attempt-a",
                "video-a",
                Stamp.AddMinutes(5),
                CancellationToken.None
            )
        );
        await Require(
            outbox.CompleteAsync(
                "attempt-b",
                "video-b",
                Stamp.AddMinutes(6),
                CancellationToken.None
            )
        );

        var restarted = new UploadOutbox(temp.Root);
        UploadAttemptManifest first = await ReloadAsync(restarted, "attempt-a");
        UploadAttemptManifest second = await ReloadAsync(restarted, "attempt-b");

        Assert.Equal(replayId, first.ReplayId);
        Assert.Equal(replayId, second.ReplayId);
        Assert.NotEqual(first.AttemptId, second.AttemptId);
        Assert.Equal(firstPath, first.MediaPath);
        Assert.Equal(secondPath, second.MediaPath);
        Assert.Equal("hash-a", first.MediaHash);
        Assert.Equal("hash-b", second.MediaHash);
        Assert.Equal("video-a", first.VideoId);
        Assert.Equal("video-b", second.VideoId);
        Assert.NotEqual(Path.Combine(temp.Root, "100"), restarted.AttemptDirectory("attempt-a"));
        Assert.NotEqual(Path.Combine(temp.Root, "100"), restarted.AttemptDirectory("attempt-b"));
        AssertNoLegacyReceipts(temp.Root);
    }

    [Fact]
    public async Task MissingOrNegativeReplayId_RoundTripsByAttemptId()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        string missingPath = Path.Combine(temp.Root, "attempt-missing", "final.mp4");
        string localPath = Path.Combine(temp.Root, "attempt-local", "final.mp4");
        string decoyDirectory = Path.Combine(temp.Root, "-1");
        Directory.CreateDirectory(decoyDirectory);
        File.WriteAllBytes(Path.Combine(decoyDirectory, "newest.mp4"), new byte[] { 1, 2, 3 });

        await ReachPendingAsync(outbox, "attempt-missing", null, missingPath, "hash-missing");
        await ReachPendingAsync(outbox, "attempt-local", -1, localPath, "hash-local");

        var restarted = new UploadOutbox(temp.Root);
        UploadAttemptManifest missing = await ReloadAsync(restarted, "attempt-missing");
        UploadAttemptManifest local = await ReloadAsync(restarted, "attempt-local");
        IReadOnlyList<UploadAttemptManifest> open = await restarted.ListOpenAsync(
            CancellationToken.None
        );

        Assert.Null(missing.ReplayId);
        Assert.Equal(-1, local.ReplayId);
        Assert.Equal(missingPath, missing.MediaPath);
        Assert.Equal(localPath, local.MediaPath);
        Assert.Equal(2, open.Count);
        Assert.DoesNotContain(
            open,
            item => item.MediaPath.EndsWith("newest.mp4", StringComparison.Ordinal)
        );
        Assert.Equal(
            missingPath,
            await restarted.SelectRestartMediaAsync("attempt-missing", CancellationToken.None)
        );
        Assert.Equal(
            localPath,
            await restarted.SelectRestartMediaAsync("attempt-local", CancellationToken.None)
        );
        Assert.False(UploadAttemptReceipt.IsProduction(missing));
        Assert.False(UploadAttemptReceipt.IsProduction(local));
    }

    [Fact]
    public async Task Restart_DoesNotSelectANewerUnboundMp4()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        const int replayId = 42;
        string bound = Path.Combine(temp.Root, "attempt-bound", "final.mp4");
        string newer = Path.Combine(temp.Root, "attempt-bound", "newer.mp4");
        string other = Path.Combine(temp.Root, "attempt-other", "keep.mp4");
        string decoyDirectory = Path.Combine(temp.Root, replayId.ToString());
        string decoy = Path.Combine(decoyDirectory, "stale.mp4");

        await Require(
            outbox.PrepareAsync("attempt-bound", replayId, Stamp, CancellationToken.None)
        );
        await Require(
            outbox.BeginRecordingAsync("attempt-bound", Stamp.AddMinutes(1), CancellationToken.None)
        );
        Directory.CreateDirectory(Path.GetDirectoryName(bound));
        Touch(newer, new DateTime(2026, 9, 29, 18, 0, 0, DateTimeKind.Utc), new byte[] { 2 });
        Assert.Null(await outbox.SelectRestartMediaAsync("attempt-bound", CancellationToken.None));
        Assert.Empty(await outbox.ListOpenAsync(CancellationToken.None));

        await Require(
            outbox.FinalizeMediaAsync(
                "attempt-bound",
                bound,
                128,
                "hash-bound",
                Stamp.AddMinutes(2),
                CancellationToken.None
            )
        );
        await Require(
            outbox.MarkUploadPendingAsync(
                "attempt-bound",
                Stamp.AddMinutes(3),
                CancellationToken.None
            )
        );
        await Require(
            outbox.DispatchAsync(
                "attempt-bound",
                youtubeEnabled: true,
                dryRun: false,
                operatorRetry: false,
                Stamp.AddMinutes(4),
                CancellationToken.None
            )
        );
        Touch(bound, new DateTime(2026, 9, 28, 1, 0, 0, DateTimeKind.Utc), new byte[] { 1 });
        Directory.CreateDirectory(decoyDirectory);
        Touch(decoy, new DateTime(2026, 9, 29, 23, 0, 0, DateTimeKind.Utc), new byte[] { 9, 9, 9 });
        await ReachPendingAsync(outbox, "attempt-other", replayId, other, "hash-other");

        string newestInAttempt = Directory
            .GetFiles(Path.GetDirectoryName(bound), "*.mp4")
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .First();
        var restarted = new UploadOutbox(temp.Root);
        IReadOnlyList<UploadAttemptManifest> open = await restarted.ListOpenAsync(
            CancellationToken.None
        );

        Assert.Equal(newer, newestInAttempt);
        Assert.Equal(
            bound,
            await restarted.SelectRestartMediaAsync("attempt-bound", CancellationToken.None)
        );
        Assert.Equal(
            other,
            await restarted.SelectRestartMediaAsync("attempt-other", CancellationToken.None)
        );
        Assert.Equal(2, open.Count);
        Assert.Contains(open, item => item.MediaPath == bound && item.ReplayId == replayId);
        Assert.Contains(open, item => item.MediaPath == other && item.ReplayId == replayId);
        Assert.DoesNotContain(open, item => item.MediaPath == newer || item.MediaPath == decoy);
        Assert.Null(await restarted.SelectRestartMediaAsync("42", CancellationToken.None));
    }

    [Fact]
    public async Task DryRun_IsNotAProductionUpload()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        string mediaPath = Path.Combine(temp.Root, "attempt-dry", "final.mp4");
        await ReachPendingAsync(outbox, "attempt-dry", 7, mediaPath, "hash-dry");

        UploadAttemptResult simulated = await outbox.DispatchAsync(
            "attempt-dry",
            youtubeEnabled: true,
            dryRun: true,
            operatorRetry: false,
            Stamp.AddMinutes(4),
            CancellationToken.None
        );
        UploadAttemptResult completed = await outbox.CompleteAsync(
            "attempt-dry",
            "video-should-not-exist",
            Stamp.AddMinutes(5),
            CancellationToken.None
        );
        var restarted = new UploadOutbox(temp.Root);
        UploadAttemptResult loaded = await restarted.LoadAsync(
            "attempt-dry",
            CancellationToken.None
        );
        string json = File.ReadAllText(restarted.ManifestPath("attempt-dry"));

        Assert.True(simulated.Succeeded, simulated.Reason);
        Assert.Equal(UploadAttemptState.DryRunSimulated, simulated.Manifest.State);
        Assert.Null(simulated.Manifest.VideoId);
        Assert.Equal(UploadAttemptReceiptKind.Simulation, simulated.Manifest.ReceiptKind);
        Assert.True(UploadAttemptReceipt.IsSimulation(simulated.Manifest));
        Assert.False(UploadAttemptReceipt.IsProduction(simulated.Manifest));
        Assert.False(completed.Succeeded);
        Assert.Equal(UploadAttemptReasons.IllegalTransition, completed.Reason);
        Assert.True(loaded.Succeeded, loaded.Reason);
        Assert.Equal(UploadAttemptState.DryRunSimulated, loaded.Manifest.State);
        Assert.Null(loaded.Manifest.VideoId);
        Assert.False(UploadAttemptReceipt.IsProduction(loaded.Manifest));
        Assert.DoesNotContain("\"ReceiptKind\": \"production\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"State\": \"Uploaded\"", json, StringComparison.Ordinal);
        AssertNoLegacyReceipts(temp.Root);
    }

    [Fact]
    public async Task SaveDispatch_PersistsDryRunAndLeavesTheSecondCallSettled()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        string mediaPath = Path.Combine(temp.Root, "clip.mp4");

        SavedDispatch first = await outbox.SaveDispatchAsync(
            "replay-7",
            7,
            mediaPath,
            128,
            "len-128",
            youtubeEnabled: true,
            dryRun: true,
            Stamp,
            CancellationToken.None
        );
        SavedDispatch second = await outbox.SaveDispatchAsync(
            "replay-7",
            7,
            mediaPath,
            128,
            "len-128",
            youtubeEnabled: true,
            dryRun: false,
            Stamp.AddMinutes(1),
            CancellationToken.None
        );

        Assert.True(first.Result.Succeeded, first.Result.Reason);
        Assert.False(first.AlreadySettled);
        Assert.False(first.MaySend);
        Assert.Equal(UploadAttemptState.DryRunSimulated, first.Result.Manifest.State);
        Assert.True(File.Exists(outbox.ManifestPath("replay-7")));
        Assert.True(second.Result.Succeeded, second.Result.Reason);
        Assert.True(second.AlreadySettled);
        Assert.False(second.MaySend);
        Assert.Equal(UploadAttemptState.DryRunSimulated, second.Result.Manifest.State);
        Assert.Equal(first.Result.Manifest.Revision, second.Result.Manifest.Revision);
        Assert.Null(second.Result.Manifest.VideoId);
    }

    [Fact]
    public async Task InterruptedUpload_DoesNotSendAgainUntilAnOperatorRetries()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        string mediaPath = Path.Combine(temp.Root, "match.mp4");
        const string mediaHash = "len-128";

        SavedDispatch started = await outbox.SaveDispatchAsync(
            "replay-11",
            11,
            mediaPath,
            128,
            mediaHash,
            youtubeEnabled: true,
            dryRun: false,
            Stamp,
            CancellationToken.None
        );
        SavedDispatch interrupted = await outbox.SaveDispatchAsync(
            "replay-11",
            11,
            Path.Combine(temp.Root, "other.mp4"),
            64,
            "other-hash",
            youtubeEnabled: true,
            dryRun: false,
            Stamp.AddMinutes(1),
            CancellationToken.None
        );
        SavedDispatch ordinary = await outbox.SaveDispatchAsync(
            "replay-11",
            11,
            mediaPath,
            128,
            mediaHash,
            youtubeEnabled: true,
            dryRun: false,
            Stamp.AddMinutes(2),
            CancellationToken.None
        );
        SavedDispatch dryRetry = await outbox.SaveDispatchAsync(
            "replay-11",
            11,
            mediaPath,
            128,
            mediaHash,
            youtubeEnabled: true,
            dryRun: true,
            Stamp.AddMinutes(3),
            CancellationToken.None,
            operatorRetry: true
        );
        SavedDispatch disabledRetry = await outbox.SaveDispatchAsync(
            "replay-11",
            11,
            mediaPath,
            128,
            mediaHash,
            youtubeEnabled: false,
            dryRun: false,
            Stamp.AddMinutes(4),
            CancellationToken.None,
            operatorRetry: true
        );
        SavedDispatch allowed = await outbox.SaveDispatchAsync(
            "replay-11",
            11,
            Path.Combine(temp.Root, "other.mp4"),
            64,
            "other-hash",
            youtubeEnabled: true,
            dryRun: false,
            Stamp.AddMinutes(5),
            CancellationToken.None,
            operatorRetry: true
        );
        UploadAttemptManifest reloaded = await ReloadAsync(
            new UploadOutbox(temp.Root),
            "replay-11"
        );

        Assert.True(started.Result.Succeeded, started.Result.Reason);
        Assert.True(started.MaySend);
        Assert.False(started.AlreadySettled);
        Assert.Equal(UploadAttemptState.Uploading, started.Result.Manifest.State);
        Assert.Equal(mediaPath, started.Result.Manifest.MediaPath);
        Assert.True(interrupted.Result.Succeeded, interrupted.Result.Reason);
        Assert.False(interrupted.MaySend);
        Assert.False(interrupted.AlreadySettled);
        Assert.Equal(UploadAttemptState.AmbiguousUpload, interrupted.Result.Manifest.State);
        Assert.Equal(mediaPath, interrupted.Result.Manifest.MediaPath);
        Assert.Equal(mediaHash, interrupted.Result.Manifest.MediaHash);
        Assert.Null(interrupted.Result.Manifest.VideoId);
        Assert.True(ordinary.AlreadySettled);
        Assert.False(ordinary.MaySend);
        Assert.Equal(UploadAttemptState.AmbiguousUpload, ordinary.Result.Manifest.State);
        Assert.False(dryRetry.Result.Succeeded);
        Assert.False(dryRetry.MaySend);
        Assert.Equal(UploadAttemptReasons.DryRun, dryRetry.Result.Reason);
        Assert.Equal(UploadAttemptState.AmbiguousUpload, dryRetry.Result.Manifest.State);
        Assert.False(disabledRetry.Result.Succeeded);
        Assert.False(disabledRetry.MaySend);
        Assert.Equal(UploadAttemptReasons.YoutubeDisabled, disabledRetry.Result.Reason);
        Assert.Equal(UploadAttemptState.AmbiguousUpload, disabledRetry.Result.Manifest.State);
        Assert.True(allowed.Result.Succeeded, allowed.Result.Reason);
        Assert.True(allowed.MaySend);
        Assert.False(allowed.AlreadySettled);
        Assert.Equal(UploadAttemptState.Uploading, allowed.Result.Manifest.State);
        Assert.Equal(mediaPath, allowed.Result.Manifest.MediaPath);
        Assert.Equal(mediaHash, allowed.Result.Manifest.MediaHash);
        Assert.Null(allowed.Result.Manifest.VideoId);
        Assert.Equal(UploadAttemptState.Uploading, reloaded.State);
        Assert.Equal(mediaPath, reloaded.MediaPath);
        Assert.Null(reloaded.VideoId);
    }

    [Fact]
    public async Task Disabled_NeverUploads()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        await ReachPendingAsync(
            outbox,
            "attempt-off",
            7,
            Path.Combine(temp.Root, "attempt-off", "final.mp4"),
            "hash-off"
        );
        await ReachPendingAsync(
            outbox,
            "attempt-off-dry",
            7,
            Path.Combine(temp.Root, "attempt-off-dry", "final.mp4"),
            "hash-off-dry"
        );

        UploadAttemptResult disabled = await outbox.DispatchAsync(
            "attempt-off",
            youtubeEnabled: false,
            dryRun: false,
            operatorRetry: false,
            Stamp.AddMinutes(4),
            CancellationToken.None
        );
        UploadAttemptResult disabledDryRun = await outbox.DispatchAsync(
            "attempt-off-dry",
            youtubeEnabled: false,
            dryRun: true,
            operatorRetry: true,
            Stamp.AddMinutes(4),
            CancellationToken.None
        );
        UploadAttemptResult completed = await outbox.CompleteAsync(
            "attempt-off",
            "video-should-not-exist",
            Stamp.AddMinutes(5),
            CancellationToken.None
        );
        var restarted = new UploadOutbox(temp.Root);
        UploadAttemptManifest loaded = await ReloadAsync(restarted, "attempt-off");
        UploadAttemptManifest loadedDryRun = await ReloadAsync(restarted, "attempt-off-dry");

        Assert.True(disabled.Succeeded, disabled.Reason);
        Assert.Equal(UploadAttemptState.Disabled, disabled.Manifest.State);
        Assert.Null(disabled.Manifest.VideoId);
        Assert.False(UploadAttemptReceipt.IsProduction(disabled.Manifest));
        Assert.NotEqual(UploadAttemptState.Uploading, disabled.Manifest.State);
        Assert.False(completed.Succeeded);
        Assert.Equal(UploadAttemptState.Disabled, loaded.State);
        Assert.Null(loaded.VideoId);
        Assert.False(UploadAttemptReceipt.IsProduction(loaded));
        Assert.Equal(UploadAttemptState.Disabled, disabledDryRun.Manifest.State);
        Assert.Equal(UploadAttemptState.Disabled, loadedDryRun.State);
        Assert.False(UploadAttemptReceipt.IsProduction(loadedDryRun));
        Assert.False(UploadAttemptReceipt.IsSimulation(loadedDryRun));
        AssertNoLegacyReceipts(temp.Root);
    }

    [Fact]
    public async Task AmbiguousUpload_OrdinaryRetryDoesNotReturnToUploading()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        string mediaPath = Path.Combine(temp.Root, "attempt-unknown", "final.mp4");
        await ReachUploadingAsync(outbox, "attempt-unknown", 11, mediaPath, "hash-unknown");
        UploadAttemptResult ambiguous = await outbox.MarkAmbiguousAsync(
            "attempt-unknown",
            Stamp.AddMinutes(5),
            CancellationToken.None
        );

        UploadAttemptResult ordinary = await outbox.DispatchAsync(
            "attempt-unknown",
            youtubeEnabled: true,
            dryRun: false,
            operatorRetry: false,
            Stamp.AddMinutes(6),
            CancellationToken.None
        );
        UploadAttemptResult completed = await outbox.CompleteAsync(
            "attempt-unknown",
            "video-minted",
            Stamp.AddMinutes(7),
            CancellationToken.None
        );
        UploadAttemptResult disabledRetry = await outbox.DispatchAsync(
            "attempt-unknown",
            youtubeEnabled: false,
            dryRun: false,
            operatorRetry: true,
            Stamp.AddMinutes(8),
            CancellationToken.None
        );
        UploadAttemptResult dryRetry = await outbox.DispatchAsync(
            "attempt-unknown",
            youtubeEnabled: true,
            dryRun: true,
            operatorRetry: true,
            Stamp.AddMinutes(9),
            CancellationToken.None
        );
        UploadAttemptResult allowed = await outbox.DispatchAsync(
            "attempt-unknown",
            youtubeEnabled: true,
            dryRun: false,
            operatorRetry: true,
            Stamp.AddMinutes(10),
            CancellationToken.None
        );

        Assert.True(ambiguous.Succeeded, ambiguous.Reason);
        Assert.Equal(UploadAttemptState.AmbiguousUpload, ambiguous.Manifest.State);
        Assert.Null(ambiguous.Manifest.VideoId);
        Assert.False(ordinary.Succeeded);
        Assert.Equal(UploadAttemptReasons.OperatorRetryRequired, ordinary.Reason);
        Assert.Equal(UploadAttemptState.AmbiguousUpload, ordinary.Manifest.State);
        Assert.False(completed.Succeeded);
        Assert.Equal(UploadAttemptReasons.IllegalTransition, completed.Reason);
        Assert.False(disabledRetry.Succeeded);
        Assert.Equal(UploadAttemptReasons.YoutubeDisabled, disabledRetry.Reason);
        Assert.False(dryRetry.Succeeded);
        Assert.Equal(UploadAttemptReasons.DryRun, dryRetry.Reason);
        Assert.True(allowed.Succeeded, allowed.Reason);
        Assert.Equal(UploadAttemptState.Uploading, allowed.Manifest.State);
        Assert.Null(allowed.Manifest.VideoId);
        Assert.False(UploadAttemptReceipt.IsProduction(allowed.Manifest));

        UploadAttemptManifest reloaded = await ReloadAsync(
            new UploadOutbox(temp.Root),
            "attempt-unknown"
        );
        Assert.Equal(UploadAttemptState.Uploading, reloaded.State);
        Assert.Null(reloaded.VideoId);
        Assert.Equal(mediaPath, reloaded.MediaPath);
    }

    [Fact]
    public async Task Reconcile_RecordsTheKnownVideoWithoutUploadingAgain()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        await ReachUploadingAsync(
            outbox,
            "attempt-reconcile",
            11,
            Path.Combine(temp.Root, "attempt-reconcile", "final.mp4"),
            "hash-reconcile"
        );
        await Require(
            outbox.MarkAmbiguousAsync(
                "attempt-reconcile",
                Stamp.AddMinutes(5),
                CancellationToken.None
            )
        );

        UploadAttemptResult reconciled = await outbox.ReconcileAsync(
            "attempt-reconcile",
            "video-found",
            Stamp.AddMinutes(6),
            CancellationToken.None
        );
        UploadAttemptResult second = await outbox.ReconcileAsync(
            "attempt-reconcile",
            "video-other",
            Stamp.AddMinutes(7),
            CancellationToken.None
        );
        UploadAttemptManifest loaded = await ReloadAsync(
            new UploadOutbox(temp.Root),
            "attempt-reconcile"
        );

        Assert.True(reconciled.Succeeded, reconciled.Reason);
        Assert.Equal(UploadAttemptState.Uploaded, reconciled.Manifest.State);
        Assert.Equal("video-found", reconciled.Manifest.VideoId);
        Assert.True(UploadAttemptReceipt.IsProduction(reconciled.Manifest));
        Assert.False(second.Succeeded);
        Assert.Equal(UploadAttemptReasons.VideoIdConflict, second.Reason);
        Assert.Equal("video-found", loaded.VideoId);
        Assert.Equal(UploadAttemptState.Uploaded, loaded.State);
    }

    [Fact]
    public async Task SecondVideoId_IsRejected()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        await ReachUploadingAsync(
            outbox,
            "attempt-once",
            15,
            Path.Combine(temp.Root, "attempt-once", "final.mp4"),
            "hash-once"
        );
        UploadAttemptManifest uploaded = await Require(
            outbox.CompleteAsync(
                "attempt-once",
                "video-kept",
                Stamp.AddMinutes(5),
                CancellationToken.None
            )
        );
        UploadAttemptResult second = await outbox.CompleteAsync(
            "attempt-once",
            "video-other",
            Stamp.AddMinutes(6),
            CancellationToken.None
        );
        UploadAttemptManifest loaded = await ReloadAsync(
            new UploadOutbox(temp.Root),
            "attempt-once"
        );

        Assert.False(second.Succeeded);
        Assert.Equal(UploadAttemptReasons.VideoIdConflict, second.Reason);
        Assert.Equal("video-kept", second.Manifest.VideoId);
        Assert.Equal(uploaded.Revision, second.Manifest.Revision);
        Assert.Equal("video-kept", loaded.VideoId);
        Assert.Equal(uploaded.Revision, loaded.Revision);
        Assert.Equal(UploadAttemptState.Uploaded, loaded.State);
        Assert.True(UploadAttemptReceipt.IsProduction(loaded));
    }

    [Fact]
    public async Task UploadedReceipt_StoresVideoIdAtomically()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        string attemptId = "attempt-receipt";
        string mediaPath = Path.Combine(temp.Root, attemptId, "final.mp4");
        await ReachUploadingAsync(outbox, attemptId, 21, mediaPath, "hash-receipt");
        UploadAttemptManifest uploaded = await Require(
            outbox.CompleteAsync(
                attemptId,
                "video-kept",
                Stamp.AddMinutes(5),
                CancellationToken.None
            )
        );
        string manifestPath = outbox.ManifestPath(attemptId);
        string directory = outbox.AttemptDirectory(attemptId);
        Assert.DoesNotContain(
            Directory.GetFiles(directory),
            path => Path.GetFileName(path).EndsWith(".tmp", StringComparison.Ordinal)
        );

        string planted = manifestPath + ".planted.tmp";
        File.WriteAllText(
            planted,
            "{\"State\":\"Uploaded\",\"VideoId\":\"video-from-temp\",\"ReceiptKind\":\"production\"}"
        );
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        UploadAttemptManifest loaded = await ReloadAsync(new UploadOutbox(temp.Root), attemptId);

        Assert.Equal("Uploaded", document.RootElement.GetProperty("State").GetString());
        Assert.Equal("video-kept", document.RootElement.GetProperty("VideoId").GetString());
        Assert.Equal("production", document.RootElement.GetProperty("ReceiptKind").GetString());
        Assert.Equal(mediaPath, document.RootElement.GetProperty("MediaPath").GetString());
        Assert.Equal("video-kept", loaded.VideoId);
        Assert.Equal(uploaded.Revision, loaded.Revision);
        Assert.True(UploadAttemptReceipt.IsProduction(loaded));
        Assert.True(File.Exists(planted));
        AssertNoLegacyReceipts(temp.Root);
    }

    [Fact]
    public async Task CorruptManifest_IsNotUploaded()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        string attemptId = "attempt-corrupt";
        string mediaPath = Path.Combine(temp.Root, attemptId, "final.mp4");
        await ReachUploadingAsync(outbox, attemptId, 4, mediaPath, "hash-corrupt");
        await Require(
            outbox.CompleteAsync(
                attemptId,
                "video-kept",
                Stamp.AddMinutes(5),
                CancellationToken.None
            )
        );
        string manifestPath = outbox.ManifestPath(attemptId);
        string newer = Path.Combine(temp.Root, attemptId, "newer.mp4");
        Touch(newer, new DateTime(2026, 9, 29, 20, 0, 0, DateTimeKind.Utc), new byte[] { 4, 4 });

        string[] payloads =
        {
            string.Empty,
            "{",
            "not-json",
            "[]",
            "{}",
            "{\"Schema\":1,\"AttemptId\":\""
                + attemptId
                + "\",\"State\":\"Uploaded\",\"VideoId\":\"video-kept\",\"Revision\":2,\"UpdatedAtUtc\":\"2026-09-29T03:04:05+00:00\",\"MediaPath\":\""
                + mediaPath.Replace("\\", "\\\\")
                + "\",\"MediaSize\":4096,\"MediaHash\":\"hash-corrupt\",\"ReceiptKind\":\"simulation\"}",
            "{\"Schema\":1,\"AttemptId\":\""
                + attemptId
                + "\",\"State\":\"Uploaded\",\"VideoId\":\"\",\"Revision\":2,\"UpdatedAtUtc\":\"2026-09-29T03:04:05+00:00\",\"MediaPath\":\""
                + mediaPath.Replace("\\", "\\\\")
                + "\",\"MediaSize\":4096,\"MediaHash\":\"hash-corrupt\",\"ReceiptKind\":\"production\"}",
            "{\"Schema\":2,\"AttemptId\":\""
                + attemptId
                + "\",\"State\":\"Uploaded\",\"VideoId\":\"video-kept\",\"Revision\":2,\"UpdatedAtUtc\":\"2026-09-29T03:04:05+00:00\",\"MediaPath\":\""
                + mediaPath.Replace("\\", "\\\\")
                + "\",\"MediaSize\":4096,\"MediaHash\":\"hash-corrupt\",\"ReceiptKind\":\"production\"}",
            "{\"Schema\":1,\"AttemptId\":\""
                + attemptId
                + "\",\"State\":5,\"VideoId\":\"video-kept\",\"Revision\":2,\"UpdatedAtUtc\":\"2026-09-29T03:04:05+00:00\",\"MediaPath\":\""
                + mediaPath.Replace("\\", "\\\\")
                + "\",\"MediaSize\":4096,\"MediaHash\":\"hash-corrupt\",\"ReceiptKind\":\"production\"}",
        };

        foreach (string payload in payloads)
        {
            File.WriteAllText(manifestPath, payload);
            UploadAttemptResult loaded = await new UploadOutbox(temp.Root).LoadAsync(
                attemptId,
                CancellationToken.None
            );

            Assert.False(loaded.Succeeded, payload);
            Assert.Equal(UploadAttemptReasons.ManifestCorrupt, loaded.Reason);
            Assert.Null(loaded.Manifest);
            Assert.False(UploadAttemptReceipt.IsProduction(loaded.Manifest));
            Assert.Null(
                await new UploadOutbox(temp.Root).SelectRestartMediaAsync(
                    attemptId,
                    CancellationToken.None
                )
            );
        }
    }

    [Fact]
    public async Task Restart_ReloadsUploadedVideoId()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        string mediaPath = Path.Combine(temp.Root, "attempt-restart", "final.mp4");
        DateTimeOffset uploadedAt = Stamp.AddMinutes(5);
        await ReachUploadingAsync(outbox, "attempt-restart", null, mediaPath, "hash-restart");
        await Require(
            outbox.CompleteAsync(
                "attempt-restart",
                "video-kept",
                uploadedAt,
                CancellationToken.None
            )
        );

        UploadAttemptManifest loaded = await ReloadAsync(
            new UploadOutbox(temp.Root),
            "attempt-restart"
        );

        Assert.Equal(UploadAttemptState.Uploaded, loaded.State);
        Assert.Equal("video-kept", loaded.VideoId);
        Assert.Equal(mediaPath, loaded.MediaPath);
        Assert.Equal(4096, loaded.MediaSize);
        Assert.Equal("hash-restart", loaded.MediaHash);
        Assert.Null(loaded.ReplayId);
        Assert.Equal(uploadedAt, loaded.UpdatedAtUtc);
        Assert.Equal(UploadAttemptReceiptKind.Production, loaded.ReceiptKind);
        Assert.True(UploadAttemptReceipt.IsProduction(loaded));
    }

    [Fact]
    public async Task Save_ConflictDoesNotOverwriteUploaded()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        string attemptId = "attempt-race";
        string mediaPath = Path.Combine(temp.Root, attemptId, "final.mp4");
        UploadAttemptManifest uploading = await ReachUploadingAsync(
            outbox,
            attemptId,
            3,
            mediaPath,
            "hash-race"
        );
        long seen = uploading.Revision;
        bool forged = false;
        UploadAttemptResult forgedResult = await outbox.AdvanceFromAsync(
            attemptId,
            seen,
            current =>
            {
                forged = true;
                return UploadAttemptResult.Success(
                    new UploadAttemptManifest
                    {
                        Schema = UploadAttemptManifest.SchemaVersion,
                        AttemptId = current.AttemptId,
                        ReplayId = current.ReplayId,
                        State = UploadAttemptState.Uploaded,
                        MediaPath = current.MediaPath + ".swapped.mp4",
                        MediaSize = current.MediaSize,
                        MediaHash = current.MediaHash,
                        VideoId = "video-forged",
                        Revision = current.Revision,
                        UpdatedAtUtc = Stamp,
                        ReceiptKind = UploadAttemptReceiptKind.Production,
                    }
                );
            },
            CancellationToken.None
        );
        UploadAttemptManifest afterForgery = await ReloadAsync(outbox, attemptId);
        await Require(
            outbox.CompleteAsync(
                attemptId,
                "video-kept",
                Stamp.AddMinutes(5),
                CancellationToken.None
            )
        );
        bool stale = false;
        UploadAttemptResult conflict = await outbox.AdvanceFromAsync(
            attemptId,
            seen,
            current =>
            {
                stale = true;
                return UploadAttemptMachine.MarkAmbiguous(current, Stamp.AddMinutes(6));
            },
            CancellationToken.None
        );
        UploadAttemptResult preparedAgain = await outbox.PrepareAsync(
            attemptId,
            3,
            Stamp.AddMinutes(7),
            CancellationToken.None
        );
        UploadAttemptManifest loaded = await ReloadAsync(new UploadOutbox(temp.Root), attemptId);

        Assert.True(forged);
        Assert.False(forgedResult.Succeeded);
        Assert.Equal(UploadAttemptReasons.IllegalTransition, forgedResult.Reason);
        Assert.Equal(UploadAttemptState.Uploading, afterForgery.State);
        Assert.Equal(mediaPath, afterForgery.MediaPath);
        Assert.Equal(seen, afterForgery.Revision);
        Assert.False(stale);
        Assert.False(conflict.Succeeded);
        Assert.Equal(UploadAttemptReasons.ManifestConflict, conflict.Reason);
        Assert.Equal("video-kept", conflict.Manifest.VideoId);
        Assert.Equal(UploadAttemptState.Uploaded, conflict.Manifest.State);
        Assert.False(preparedAgain.Succeeded);
        Assert.Equal(UploadAttemptReasons.ManifestConflict, preparedAgain.Reason);
        Assert.Equal("video-kept", preparedAgain.Manifest.VideoId);
        Assert.Equal("video-kept", loaded.VideoId);
        Assert.Equal(mediaPath, loaded.MediaPath);
        Assert.True(UploadAttemptReceipt.IsProduction(loaded));
    }

    [Fact]
    public async Task TwoWorkers_OneWriteLeavesASingleVideoId()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        string attemptId = "attempt-workers";
        UploadAttemptManifest uploading = await ReachUploadingAsync(
            outbox,
            attemptId,
            8,
            Path.Combine(temp.Root, attemptId, "final.mp4"),
            "hash-workers"
        );
        int calls = 0;
        Task<UploadAttemptResult> first = outbox.AdvanceFromAsync(
            attemptId,
            uploading.Revision,
            current =>
            {
                Interlocked.Increment(ref calls);
                return UploadAttemptMachine.Complete(current, "video-one", Stamp.AddMinutes(5));
            },
            CancellationToken.None
        );
        Task<UploadAttemptResult> second = outbox.AdvanceFromAsync(
            attemptId,
            uploading.Revision,
            current =>
            {
                Interlocked.Increment(ref calls);
                return UploadAttemptMachine.Complete(current, "video-two", Stamp.AddMinutes(5));
            },
            CancellationToken.None
        );

        UploadAttemptResult[] results = await Task.WhenAll(first, second);
        UploadAttemptResult winner = Assert.Single(results, result => result.Succeeded);
        UploadAttemptResult loser = Assert.Single(results, result => !result.Succeeded);
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(outbox.ManifestPath(attemptId))
        );
        UploadAttemptManifest loaded = await ReloadAsync(new UploadOutbox(temp.Root), attemptId);

        Assert.Equal(1, calls);
        Assert.Equal(UploadAttemptReasons.ManifestConflict, loser.Reason);
        Assert.Equal(winner.Manifest.VideoId, loaded.VideoId);
        Assert.Equal(loaded.VideoId, document.RootElement.GetProperty("VideoId").GetString());
        Assert.Equal("Uploaded", document.RootElement.GetProperty("State").GetString());
        Assert.True(loaded.VideoId == "video-one" || loaded.VideoId == "video-two");
        Assert.True(UploadAttemptReceipt.IsProduction(loaded));
    }

    [Fact]
    public async Task Finalize_RejectsIncompleteMedia()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        string attemptId = "attempt-media";
        await Require(outbox.PrepareAsync(attemptId, 1, Stamp, CancellationToken.None));
        await Require(
            outbox.BeginRecordingAsync(attemptId, Stamp.AddMinutes(1), CancellationToken.None)
        );
        Directory.CreateDirectory(outbox.AttemptDirectory(attemptId));
        Touch(
            Path.Combine(outbox.AttemptDirectory(attemptId), "newer.mp4"),
            new DateTime(2026, 9, 29, 4, 0, 0, DateTimeKind.Utc),
            new byte[] { 1 }
        );

        UploadAttemptResult emptyPath = await outbox.FinalizeMediaAsync(
            attemptId,
            " ",
            10,
            "hash",
            Stamp.AddMinutes(2),
            CancellationToken.None
        );
        UploadAttemptResult emptySize = await outbox.FinalizeMediaAsync(
            attemptId,
            Path.Combine(temp.Root, attemptId, "final.mp4"),
            0,
            "hash",
            Stamp.AddMinutes(2),
            CancellationToken.None
        );
        UploadAttemptResult emptyHash = await outbox.FinalizeMediaAsync(
            attemptId,
            Path.Combine(temp.Root, attemptId, "final.mp4"),
            10,
            " ",
            Stamp.AddMinutes(2),
            CancellationToken.None
        );

        Assert.False(emptyPath.Succeeded);
        Assert.Equal(UploadAttemptReasons.MediaIncomplete, emptyPath.Reason);
        Assert.Equal(UploadAttemptState.Recording, emptyPath.Manifest.State);
        Assert.False(emptySize.Succeeded);
        Assert.Equal(UploadAttemptReasons.MediaIncomplete, emptySize.Reason);
        Assert.False(emptyHash.Succeeded);
        Assert.Equal(UploadAttemptReasons.MediaIncomplete, emptyHash.Reason);
        Assert.Empty(await outbox.ListOpenAsync(CancellationToken.None));
        Assert.Null(await outbox.SelectRestartMediaAsync(attemptId, CancellationToken.None));
    }

    [Fact]
    public async Task Complete_BeforeTheOutbox_DoesNotUpload()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        string attemptId = "attempt-early";
        await ReachPendingAsync(
            outbox,
            attemptId,
            9,
            Path.Combine(temp.Root, attemptId, "final.mp4"),
            "hash-early"
        );

        UploadAttemptResult completed = await outbox.CompleteAsync(
            attemptId,
            "video-skip",
            Stamp.AddMinutes(5),
            CancellationToken.None
        );
        UploadAttemptManifest loaded = await ReloadAsync(outbox, attemptId);

        Assert.False(completed.Succeeded);
        Assert.Equal(UploadAttemptReasons.IllegalTransition, completed.Reason);
        Assert.Equal(UploadAttemptState.UploadPending, loaded.State);
        Assert.Null(loaded.VideoId);
        Assert.False(UploadAttemptReceipt.IsProduction(loaded));
    }

    [Fact]
    public async Task InterruptedSend_KeepsAUniqueContextAndDoesNotInsertAgain()
    {
        using var temp = new TempAttempts();
        var outbox = new UploadOutbox(temp.Root);
        string mediaPath = Path.Combine(temp.Root, "match.mp4");
        const string sessionUri =
            "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&upload_id=test-session";
        string firstContext = UploadAttemptIds.NewContext(44, Stamp);
        string laterContext = UploadAttemptIds.NewContext(44, Stamp.AddSeconds(1));

        SavedDispatch started = await outbox.SaveDispatchAsync(
            firstContext,
            44,
            mediaPath,
            128,
            "len-128",
            youtubeEnabled: true,
            dryRun: false,
            Stamp,
            CancellationToken.None
        );
        UploadAttemptResult noted = await outbox.NoteSessionAsync(
            firstContext,
            sessionUri,
            Stamp.AddMinutes(1),
            CancellationToken.None
        );
        SavedDispatch ordinary = await outbox.SaveDispatchAsync(
            firstContext,
            44,
            mediaPath,
            128,
            "len-128",
            youtubeEnabled: true,
            dryRun: false,
            Stamp.AddMinutes(2),
            CancellationToken.None
        );
        string reused = await new UploadOutbox(temp.Root).ContextForReplayAsync(
            44,
            Stamp.AddMinutes(3),
            CancellationToken.None
        );
        UploadAttemptManifest reloaded = await ReloadAsync(
            new UploadOutbox(temp.Root),
            firstContext
        );
        UploadAttemptResult prepared = UploadAttemptMachine.NoteSession(
            new UploadAttemptManifest
            {
                Schema = UploadAttemptManifest.SchemaVersion,
                AttemptId = "replay-1",
                State = UploadAttemptState.UploadPending,
                MediaPath = mediaPath,
                MediaSize = 128,
                MediaHash = "len-128",
                Revision = 1,
                UpdatedAtUtc = Stamp,
            },
            sessionUri,
            Stamp
        );

        Assert.NotEqual(firstContext, laterContext);
        Assert.StartsWith("replay-44-", firstContext, StringComparison.Ordinal);
        Assert.NotEqual("replay-44", firstContext);
        Assert.True(started.MaySend);
        Assert.True(noted.Succeeded, noted.Reason);
        Assert.Equal(sessionUri, noted.Manifest.SessionUri);
        Assert.False(ordinary.MaySend);
        Assert.Equal(UploadAttemptState.AmbiguousUpload, ordinary.Result.Manifest.State);
        Assert.Equal(sessionUri, ordinary.Result.Manifest.SessionUri);
        Assert.Equal(firstContext, reused);
        Assert.Equal(sessionUri, reloaded.SessionUri);
        Assert.Equal(UploadAttemptState.AmbiguousUpload, reloaded.State);
        Assert.False(prepared.Succeeded);
        Assert.Equal(1, Directory.GetDirectories(temp.Root).Length);
    }

    private static async Task<UploadAttemptManifest> ReachPendingAsync(
        UploadOutbox outbox,
        string attemptId,
        int? replayId,
        string mediaPath,
        string mediaHash
    )
    {
        await Require(outbox.PrepareAsync(attemptId, replayId, Stamp, CancellationToken.None));
        await Require(
            outbox.BeginRecordingAsync(attemptId, Stamp.AddMinutes(1), CancellationToken.None)
        );
        await Require(
            outbox.FinalizeMediaAsync(
                attemptId,
                mediaPath,
                4096,
                mediaHash,
                Stamp.AddMinutes(2),
                CancellationToken.None
            )
        );
        return await Require(
            outbox.MarkUploadPendingAsync(attemptId, Stamp.AddMinutes(3), CancellationToken.None)
        );
    }

    private static async Task<UploadAttemptManifest> ReachUploadingAsync(
        UploadOutbox outbox,
        string attemptId,
        int? replayId,
        string mediaPath,
        string mediaHash
    )
    {
        await ReachPendingAsync(outbox, attemptId, replayId, mediaPath, mediaHash);
        return await Require(
            outbox.DispatchAsync(
                attemptId,
                youtubeEnabled: true,
                dryRun: false,
                operatorRetry: false,
                Stamp.AddMinutes(4),
                CancellationToken.None
            )
        );
    }

    private static async Task<UploadAttemptManifest> ReloadAsync(
        UploadOutbox outbox,
        string attemptId
    )
    {
        UploadAttemptResult loaded = await outbox.LoadAsync(attemptId, CancellationToken.None);
        Assert.True(loaded.Succeeded, loaded.Reason);
        return loaded.Manifest;
    }

    private static async Task<UploadAttemptManifest> Require(Task<UploadAttemptResult> call)
    {
        UploadAttemptResult result = await call;
        Assert.True(result.Succeeded, result.Reason);
        Assert.NotNull(result.Manifest);
        return result.Manifest;
    }

    private static void Touch(string path, DateTime writtenAtUtc, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, content);
        File.SetLastWriteTimeUtc(path, writtenAtUtc);
    }

    private static void AssertNoLegacyReceipts(string root)
    {
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(path);
            Assert.NotEqual("youtube-entry.json", name);
            Assert.NotEqual("youtube-entry-uploaded.json", name);
            Assert.NotEqual("youtube-dry-run.json", name);
        }
    }

    private sealed class TempAttempts : IDisposable
    {
        public TempAttempts()
        {
            Root = Path.Combine(Path.GetTempPath(), "hr-outbox-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (!Directory.Exists(Root))
            {
                return;
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(Root, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
