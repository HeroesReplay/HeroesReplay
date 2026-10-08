using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.Telemetry;
using HeroesReplay.Core.YouTube.Metadata;
using HeroesReplay.Core.YouTube.Outbox;
using HeroesReplay.Core.YouTube.Playlists;
using HeroesReplay.Core.YouTube.Publication;
using HeroesReplay.Core.YouTube.Quota;
using HeroesReplay.Core.YouTube.Search;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.YouTube;

/// <summary>What one recording's turn in an upload pass came to.</summary>
public enum UploadOutcome
{
    /// <summary>Sent to YouTube (or, in a dry run, planned and receipted).</summary>
    Uploaded,

    /// <summary>Nothing to do: already uploaded, removed, or settled.</summary>
    Done,

    /// <summary>Correctly waiting: quota, the insert cap, a publish slot, or its entry.</summary>
    Waiting,

    /// <summary>An interrupted upload that needs an operator (its automatic retries are used).</summary>
    Parked,

    /// <summary>The send failed or was cut off. The next pass retries it.</summary>
    Failed,
}

/// <summary>
/// https://developers.google.com/youtube/v3/guides/moving_to_oauth#offlinelong-lived-access-to-the-youtube-api
/// https://developers.google.com/youtube/v3/code_samples/dotnet#upload_a_video
/// </summary>
public class YouTubeUploader : IYouTubeUploader
{
    /// <summary>
    /// On a service stop the send ends at the next chunk boundary. A chunk that has not finished
    /// by then is cut off, well inside the 20 seconds <c>services stop</c> waits before it kills.
    /// </summary>
    public static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(10);

    private const string UploadBucket = "upload-bucket";

    private readonly ILogger<YouTubeUploader> logger;
    private readonly AppSettings settings;
    private readonly CancellationTokenSource cancellationTokenSource;
    private readonly ConcurrentDictionary<string, byte> uploadsInFlight = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly IYouTubeLibrary library;
    private readonly YouTubeQuotaUnits quotaUnits;
    private readonly InterruptedUploadRetries retries;
    private readonly UploadWaitLog waits = new();
    private readonly ConcurrentDictionary<string, byte> incompleteSessions = new(
        StringComparer.Ordinal
    );
    private readonly List<DateTimeOffset> publicAtUtc = new();
    private readonly List<bool> publicRequested = new();
    private Dictionary<string, UploadAttemptManifest> interruptedInPass;
    private int insertsToday;
    private bool quotaHeld;
    private string reportedConcern;
    private string passGate;
    private string libraryProblem;
    private bool lastPassHealthy = true;
    private bool oauthLogged;
    private DateTimeOffset? lastInsertUtc;
    private DateTimeOffset? lastPublicUtc;
    private DateTimeOffset currentQuotaDay;
    private string lastMap;
    private DateTimeOffset? lastMapUtc;
    private string lastHero;
    private DateTimeOffset? lastHeroUtc;
    private bool ledgerLoaded;
    private readonly HashSet<int> joinedReplaySessions = new();

    /// <summary>
    /// Tests point this at a temp file. Production uses the shared session file.
    /// </summary>
    internal string ReplaySessionFilePath { get; set; }

    /// <summary>
    /// Healthy work for <c>services status</c>: an upload, a library pass, or a pass that
    /// correctly sends nothing. Tests count the calls; production writes the heartbeat.
    /// </summary>
    internal Action RecordWork { get; set; } = ServiceHost.ServiceHeartbeat.RecordWork;

    public YouTubeUploader(
        ILogger<YouTubeUploader> logger,
        AppSettings settings,
        CancellationTokenSource cancellationTokenSource,
        IYouTubeLibrary library = null
    )
    {
        this.logger = logger;
        this.settings = settings;
        this.cancellationTokenSource = cancellationTokenSource;
        this.library = library;
        quotaUnits = new YouTubeQuotaUnits(settings?.Location?.DataDirectory, settings?.YouTube);
        retries = new InterruptedUploadRetries(settings?.Location?.DataDirectory);
    }

    /// <summary>
    /// Joins each replay session once. The idle loop calls this every 2 seconds; a span per
    /// known session on every call added about 200 spans a second to old replay traces, and the
    /// Aspire dashboard grew to 8.5 GB on production (2026-10-08).
    /// </summary>
    internal void JoinKnownReplaySessions()
    {
        foreach (int id in ReplaySessionFile.ReadIds(ReplaySessionFilePath))
        {
            if (joinedReplaySessions.Contains(id))
            {
                continue;
            }

            using System.Diagnostics.Activity session = HoldReplaySession(id);
        }
    }

    private System.Diagnostics.Activity HoldReplaySession(int? replayId)
    {
        if (replayId is not int id || id <= 0)
        {
            return null;
        }

        System.Diagnostics.Activity joined = ReplaySessionFile.Join(
            id,
            "heroesreplay.session.joined",
            ReplaySessionFilePath
        );
        if (joined == null)
        {
            return null;
        }

        if (joinedReplaySessions.Add(id))
        {
            logger.LogInformation("Replay session {ReplayId} trace {TraceId}.", id, joined.TraceId);
        }

        return joined;
    }

    private async void FileSystemWatcher_Created(object sender, FileSystemEventArgs e)
    {
        try
        {
            UploadOutcome outcome = await ProcessOnceAsync(e.FullPath).ConfigureAwait(false);
            if (outcome == UploadOutcome.Uploaded)
            {
                RecordWork?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Upload of {Path} stopped for the service stop. The next start retries it automatically.",
                e.FullPath
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not upload {Path}", e.FullPath);
        }
    }

    public async Task ListenAsync()
    {
        LogQuotaPlan();
        if (settings.YouTube.DryRun)
        {
            logger.LogInformation(
                "YouTube uploader is in dry-run. Recordings are not sent to YouTube."
            );
        }
        else
        {
            logger.LogInformation("YouTube uploader will publish recordings.");
            await AuthorizeAsync().ConfigureAwait(false);
        }

        using var recordingWatcher = new FileSystemWatcher(settings.ContextsDirectory, "*.mp4")
        {
            EnableRaisingEvents = true,
            IncludeSubdirectories = true,
        };
        recordingWatcher.Created += FileSystemWatcher_Created;
        logger.LogInformation(
            "YouTube uploader listening for mp4 files in {Directory}.",
            settings.ContextsDirectory
        );

        // The library pass has its own loop: it runs whether or not recordings wait for upload.
        Task libraryLoop = RunLibraryLoopAsync(cancellationTokenSource.Token);
        try
        {
            JoinKnownReplaySessions();
            await RunUploadPassAsync().ConfigureAwait(false);
            DateTimeOffset drained = DateTimeOffset.UtcNow;
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                // Between passes nothing changed since the last one. A healthy pass stays healthy.
                if (lastPassHealthy)
                {
                    RecordWork?.Invoke();
                }

                TimeSpan waited = TimeSpan.Zero;
                while (
                    waited < UploadDrain.Poll && !cancellationTokenSource.IsCancellationRequested
                )
                {
                    TimeSpan slice = TimeSpan.FromSeconds(2);
                    if (waited + slice > UploadDrain.Poll)
                    {
                        slice = UploadDrain.Poll - waited;
                    }

                    await Task.Delay(slice, cancellationTokenSource.Token).ConfigureAwait(false);
                    waited += slice;
                    JoinKnownReplaySessions();
                }

                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (!UploadDrain.ShouldDrain(settings.YouTube.DryRun, drained, now))
                {
                    continue;
                }

                drained = now;
                await RunUploadPassAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // shut down
        }
        finally
        {
            recordingWatcher.Created -= FileSystemWatcher_Created;
            try
            {
                await libraryLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // shut down
            }
        }
    }

    public Task ProcessRecording(string recordingPath) => ProcessRecordingAsync(recordingPath);

    internal async Task<UploadOutcome> ProcessRecordingAsync(string recordingPath)
    {
        CancellationToken token = cancellationTokenSource.Token;
        FileInfo recording = await WaitForRecordingReadyAsync(recordingPath, token)
            .ConfigureAwait(false);
        if (recording == null)
        {
            waits.Clear(recordingPath);
            return UploadOutcome.Done;
        }

        FileInfo entryFile = await WaitForEntryAsync(recording.Directory, token)
            .ConfigureAwait(false);
        if (entryFile == null)
        {
            return LogMissingEntry(recording);
        }

        YouTubeEntry entry = JsonSerializer.Deserialize<YouTubeEntry>(
            await File.ReadAllTextAsync(entryFile.FullName, token)
        );
        if (entry == null)
        {
            logger.LogWarning("YouTube entry next to {Path} was empty.", recordingPath);
            return UploadOutcome.Failed;
        }

        using System.Diagnostics.Activity replaySession = HoldReplaySession(entry.ReplayId);
        HeroesReplayTelemetry.TagReplay(replaySession, replayId: entry.ReplayId);
        YouTubeListing.Stamp(entry, settings.YouTube);
        if (string.IsNullOrWhiteSpace(entry.DesiredPrivacyStatus))
        {
            entry.DesiredPrivacyStatus = string.IsNullOrWhiteSpace(entry.PrivacyStatus)
                ? "public"
                : entry.PrivacyStatus;
        }

        UploadStaging.Apply(entry, settings.YouTube);

        if (!string.IsNullOrWhiteSpace(entry.VideoId))
        {
            waits.Clear(recording.FullName);
            RememberUploaded(entry);
            if (
                UploadVisibility.CountsAsPublic(
                    entry.ActualPrivacyStatus,
                    entry.DesiredPrivacyStatus
                )
            )
            {
                logger.LogInformation(
                    "Replay {ReplayId} already has public YouTube video {VideoId}. Marking the entry uploaded.",
                    entry.ReplayId,
                    entry.VideoId
                );
                MarkEntryUploaded(entryFile, recording.Directory);
            }
            else
            {
                logger.LogInformation(
                    "Replay {ReplayId} already has YouTube video {VideoId} at {Privacy}. It is not inserted again.",
                    entry.ReplayId,
                    entry.VideoId,
                    string.IsNullOrWhiteSpace(entry.ActualPrivacyStatus)
                        ? UploadVisibility.Staged
                        : entry.ActualPrivacyStatus
                );
            }

            return UploadOutcome.Done;
        }

        DateTimeOffset dispatchedAt = DateTimeOffset.UtcNow;
        bool enabled = settings.YouTube.Enabled != false;
        bool liveSend = enabled && !settings.YouTube.DryRun;
        if (liveSend && !QuotaHasRoom(recording.FullName, entry.Requested))
        {
            return UploadOutcome.Waiting;
        }

        var outbox = new UploadOutbox(MediaPolicyAttemptLog.AttemptsRoot(settings));
        Recovery recovery = Recovery.None;
        UploadAttemptManifest interrupted = null;
        if (liveSend)
        {
            interrupted = await FindInterruptedAsync(outbox, recording.FullName, token)
                .ConfigureAwait(false);
            if (interrupted != null)
            {
                RecoveryResult recovered = await RecoverAsync(
                        outbox,
                        interrupted,
                        recording,
                        entryFile,
                        entry,
                        token
                    )
                    .ConfigureAwait(false);
                if (recovered.Outcome is UploadOutcome done)
                {
                    return done;
                }

                recovery = recovered.How;
                interrupted = recovered.Manifest;
            }
        }

        PublicationReservationResult slot = null;
        if (enabled)
        {
            // A held slot whose time passed gets a new one before a new insert. A resumed session
            // keeps its own time, and a replay already on the channel keeps the slot its video has.
            bool reschedule = recovery != Recovery.Resume && !OnChannel(entry);
            slot = await ReservePublicationSlot(
                    entry,
                    recording.FullName,
                    dryRun: !liveSend,
                    rescheduleIfBefore: reschedule ? dispatchedAt : null
                )
                .ConfigureAwait(false);
            if (liveSend && !slot.Allow)
            {
                return slot.Kind == PublicationReservation.Terminal
                    ? UploadOutcome.Done
                    : UploadOutcome.Waiting;
            }

            UploadStaging.Schedule(entry, settings.YouTube, slot.Allow ? slot.PublishAtUtc : null);
        }

        // Signed in before the attempt moves, so a missing or broken consent leaves it as it was.
        using YouTubeService youtubeService = liveSend
            ? await CreateServiceAsync().ConfigureAwait(false)
            : null;
        string attemptId;
        if (interrupted != null)
        {
            attemptId = interrupted.AttemptId;
            if (recovery == Recovery.Reschedule)
            {
                if (InterruptedUpload.ResumeAtNewTime(slot?.PublishAtUtc, dispatchedAt))
                {
                    // The new time is now: the session's past publishAt publishes it now too.
                    recovery = Recovery.Resume;
                }
                else
                {
                    UploadAttemptResult abandoned = await outbox
                        .AbandonSessionAsync(attemptId, dispatchedAt, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (!abandoned.Succeeded)
                    {
                        logger.LogWarning(
                            "Could not drop the old upload session of {Path} ({Reason}). It stays pending.",
                            recording.FullName,
                            abandoned.Reason
                        );
                        return UploadOutcome.Failed;
                    }
                }
            }

            int used = retries.Add(attemptId, dispatchedAt);
            logger.LogInformation(
                "Upload of {Path} was interrupted. Automatic retry {Retry} of {Max}: {How}.",
                recording.FullName,
                used,
                settings.YouTube.InterruptedUploadRetries,
                DescribeRecovery(recovery, interrupted, slot?.PublishAtUtc)
            );
        }
        else
        {
            attemptId = await outbox
                .ContextForReplayAsync(entry.ReplayId, dispatchedAt, token)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(attemptId))
        {
            logger.LogWarning(
                "Upload of {Path} was not dispatched ({Reason}). It stays pending.",
                recording.FullName,
                UploadAttemptReasons.AttemptIdInvalid
            );
            return UploadOutcome.Failed;
        }

        SavedDispatch saved = await outbox
            .SaveDispatchAsync(
                attemptId,
                entry.ReplayId,
                recording.FullName,
                recording.Length,
                "len-"
                    + recording.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                settings.YouTube.Enabled != false,
                settings.YouTube.DryRun,
                dispatchedAt,
                token,
                operatorRetry: recovery == Recovery.Resume || recovery == Recovery.Restart
            )
            .ConfigureAwait(false);
        UploadAttemptResult dispatched = saved.Result;
        if (!dispatched.Succeeded)
        {
            logger.LogWarning(
                "Upload of {Path} was not dispatched ({Reason}). It stays pending.",
                recording.FullName,
                dispatched.Reason
            );
            return UploadOutcome.Failed;
        }

        if (!saved.MaySend)
        {
            if (dispatched.Manifest.State == UploadAttemptState.Disabled)
            {
                LogWait(
                    recording.FullName,
                    "disabled",
                    "YouTube is disabled. {Path} stays pending.",
                    recording.FullName
                );
                return UploadOutcome.Waiting;
            }

            if (
                !saved.AlreadySettled
                && dispatched.Manifest.State == UploadAttemptState.DryRunSimulated
            )
            {
                logger.LogInformation(
                    "Dry run for {Path} ({Bytes} bytes) as {Title}. YouTube is not called.",
                    recording.FullName,
                    recording.Length,
                    entry.Title
                );
                await CompleteDryRunAsync(recording, entry, slot, token).ConfigureAwait(false);
                return UploadOutcome.Uploaded;
            }

            if (dispatched.Manifest.State == UploadAttemptState.AmbiguousUpload)
            {
                // The open attempt went ambiguous between the lookup and the dispatch (a watcher
                // and a pass racing). The next pass recovers it.
                LogWait(
                    recording.FullName,
                    "interrupted",
                    "Upload of {Path} stopped mid-send. The next pass retries it automatically. YouTube was not called.",
                    recording.FullName
                );
                return UploadOutcome.Failed;
            }

            logger.LogInformation(
                "Upload of {Path} is already {State}. It stays pending. YouTube was not called.",
                recording.FullName,
                dispatched.Manifest.State
            );
            return UploadOutcome.Done;
        }

        return await SendAsync(
                youtubeService,
                outbox,
                attemptId,
                dispatched.Manifest,
                recording,
                entryFile,
                entry,
                token
            )
            .ConfigureAwait(false);
    }

    private async Task<UploadOutcome> SendAsync(
        YouTubeService youtubeService,
        UploadOutbox outbox,
        string attemptId,
        UploadAttemptManifest manifest,
        FileInfo recording,
        FileInfo entryFile,
        YouTubeEntry entry,
        CancellationToken token
    )
    {
        Video video = UploadBody.Build(entry);
        string session = UploadAttemptIds.IsSessionUri(manifest?.SessionUri)
            ? manifest.SessionUri
            : null;
        logger.LogInformation(
            session == null
                ? "Uploading {Path} ({Bytes} bytes) as {Title} ({Privacy}, publish at {PublishAt})."
                : "Resuming the upload of {Path} ({Bytes} bytes) as {Title} ({Privacy}, publish at {PublishAt}).",
            recording.FullName,
            recording.Length,
            entry.Title,
            video.Status.PrivacyStatus,
            video.Status.PublishAtDateTimeOffset
        );

        using FileStream fileStream = recording.Open(
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );
        VideosResource.InsertMediaUpload videosInsertRequest = youtubeService.Videos.Insert(
            video,
            UploadBody.Part,
            fileStream,
            "video/*"
        );

        // A stop ends the send between chunks, so the session keeps every byte sent and the next
        // start resumes it. A chunk that does not finish within StopGrace is cut off.
        using var send = new CancellationTokenSource();
        bool stopping = false;
        long sent = 0;
        using CancellationTokenRegistration onStop = token.Register(() =>
        {
            stopping = true;
            send.CancelAfter(StopGrace);
        });
        Video uploaded = null;
        videosInsertRequest.ProgressChanged += progress =>
        {
            sent = progress.BytesSent;
            if (progress.Exception != null)
            {
                if (stopping || progress.Exception is OperationCanceledException)
                {
                    logger.LogInformation(
                        "Upload of {Path} stopped after {Bytes} bytes.",
                        recording.FullName,
                        progress.BytesSent
                    );
                }
                else
                {
                    logger.LogError(progress.Exception, "Could not upload video.");
                }

                return;
            }

            logger.LogInformation(
                "video status: {Status}. bytes: ({Bytes})",
                progress.Status,
                progress.BytesSent
            );
            if (stopping && progress.Status == UploadStatus.Uploading)
            {
                send.Cancel();
            }
        };
        videosInsertRequest.ResponseReceived += response =>
        {
            uploaded = response;
            logger.LogInformation("YouTube upload complete id={VideoId}", response?.Id);
        };

        if (session == null)
        {
            Bookkeep(() => quotaUnits.SpendUploadCall(DateTimeOffset.UtcNow));
            try
            {
                Uri started = await videosInsertRequest
                    .InitiateSessionAsync(send.Token)
                    .ConfigureAwait(false);
                session = started?.AbsoluteUri;
            }
            catch (Exception ex)
            {
                // No session exists, so YouTube has none of the file: back to pending.
                bool returned = await ReturnUnsentAsync(outbox, attemptId).ConfigureAwait(false);
                if (ex is OperationCanceledException)
                {
                    throw new OperationCanceledException(
                        "The upload stopped before it started.",
                        ex,
                        token
                    );
                }

                if (YouTubeListQuota.IsRefused(ex) && returned)
                {
                    DateTimeOffset? until = PauseOnQuota(ex);
                    logger.LogWarning(
                        "YouTube refused the upload of {Path} before anything was sent. It stays pending and is retried after {Until:u}.",
                        recording.FullName,
                        until
                    );
                    return UploadOutcome.Waiting;
                }

                logger.LogError(
                    ex,
                    "Could not start the upload session for {Path}. Nothing was sent; it stays pending and the next pass tries again.",
                    recording.FullName
                );
                return UploadOutcome.Failed;
            }

            if (!UploadAttemptIds.IsSessionUri(session))
            {
                await ReturnUnsentAsync(outbox, attemptId).ConfigureAwait(false);
                logger.LogWarning(
                    "YouTube gave no upload session for {Path}. Nothing was sent; it stays pending.",
                    recording.FullName
                );
                return UploadOutcome.Failed;
            }

            // Saved before the first byte, so an interrupted send can always be checked and resumed.
            await NoteSessionIfPresentAsync(outbox, attemptId, session).ConfigureAwait(false);
        }

        IUploadProgress result;
        try
        {
            result = await videosInsertRequest
                .ResumeAsync(new Uri(session), send.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RecordInterruptedSendAsync(outbox, attemptId).ConfigureAwait(false);
            if (ex is OperationCanceledException)
            {
                LogStopped(recording, sent, stopping);
                throw new OperationCanceledException("The upload stopped.", ex, token);
            }

            PauseOnQuota(ex);
            logger.LogError(
                ex,
                "Upload of {Path} stopped mid-send. The next pass checks the upload session and retries it automatically.",
                recording.FullName
            );
            return UploadOutcome.Failed;
        }

        if (result.Status == UploadStatus.Completed && !string.IsNullOrWhiteSpace(uploaded?.Id))
        {
            await RecordUploadedAsync(
                    outbox,
                    attemptId,
                    uploaded,
                    recording,
                    entryFile,
                    entry,
                    reconcile: false
                )
                .ConfigureAwait(false);
            return UploadOutcome.Uploaded;
        }

        await RecordInterruptedSendAsync(outbox, attemptId).ConfigureAwait(false);
        if (stopping || result.Exception is OperationCanceledException)
        {
            LogStopped(recording, sent, stopping);
            throw new OperationCanceledException("The upload stopped.", result.Exception, token);
        }

        PauseOnQuota(result.Exception);
        logger.LogWarning(
            "Upload of {Path} stopped mid-send ({Status}). The next pass checks the upload session and retries it automatically.",
            recording.FullName,
            result.Status
        );
        return UploadOutcome.Failed;
    }

    private void LogStopped(FileInfo recording, long sent, bool stopping)
    {
        logger.LogInformation(
            stopping
                ? "Upload of {Path} paused for the service stop after {Bytes} of {Total} bytes. YouTube keeps the upload session; the next start resumes it."
                : "Upload of {Path} was cancelled after {Bytes} of {Total} bytes. The next start checks the upload session and resumes it.",
            recording.FullName,
            sent,
            recording.Length
        );
    }

    /// <summary>
    /// The video is on YouTube: from the insert response, or from a session status query after
    /// the response was lost (<paramref name="reconcile"/>).
    /// </summary>
    private async Task RecordUploadedAsync(
        UploadOutbox outbox,
        string attemptId,
        Video uploaded,
        FileInfo recording,
        FileInfo entryFile,
        YouTubeEntry entry,
        bool reconcile
    )
    {
        entry.VideoId = uploaded.Id;
        entry.ActualPrivacyStatus = string.IsNullOrWhiteSpace(uploaded.Status?.PrivacyStatus)
            ? UploadVisibility.Staged
            : uploaded.Status.PrivacyStatus;
        if (uploaded.Status?.PublishAtDateTimeOffset is DateTimeOffset publishAt)
        {
            entry.PublishAtUtc = publishAt.ToUniversalTime();
        }

        await File.WriteAllTextAsync(
                entryFile.FullName,
                JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true }),
                CancellationToken.None
            )
            .ConfigureAwait(false);
        logger.LogInformation(
            "Saved YouTube video id {VideoId} for replay {ReplayId} at {Privacy}.",
            entry.VideoId,
            entry.ReplayId,
            entry.ActualPrivacyStatus
        );
        UploadAttemptResult recorded = reconcile
            ? await outbox
                .ReconcileAsync(
                    attemptId,
                    uploaded.Id,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None
                )
                .ConfigureAwait(false)
            : await outbox
                .CompleteAsync(
                    attemptId,
                    uploaded.Id,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
        if (!recorded.Succeeded)
        {
            logger.LogWarning(
                "Could not record video {VideoId} for {Attempt} ({Reason}).",
                uploaded.Id,
                attemptId,
                recorded.Reason
            );
        }

        Bookkeep(() => retries.Clear(attemptId));
        incompleteSessions.TryRemove(attemptId, out _);
        waits.Clear(recording.FullName);
        EnsureLedger();
        if (!reconcile)
        {
            insertsToday++;
        }

        lastInsertUtc = DateTimeOffset.UtcNow;
        RememberUploaded(entry);
        Bookkeep(() =>
            YouTubeLibraryRecord.Append(
                YouTubeLibraryRecord.PathFor(settings.Location?.DataDirectory),
                YouTubeLibraryRecord.FromEntry(entry, lastInsertUtc.Value)
            )
        );
        if (
            UploadVisibility.ReconcileUntilPublic(
                entry.ActualPrivacyStatus,
                entry.DesiredPrivacyStatus
            )
        )
        {
            // Scheduled, not stuck: the library pass reads it public after publishAt (#250).
            logger.LogInformation(
                "Replay {ReplayId} uploaded as {Privacy}. YouTube publishes it at {PublishAt}. The recording can go; the library pass confirms it public after that time.",
                entry.ReplayId,
                entry.ActualPrivacyStatus ?? UploadVisibility.Staged,
                entry.PublishAtUtc
            );
        }
        else if (
            UploadVisibility.CountsAsPublic(entry.ActualPrivacyStatus, entry.DesiredPrivacyStatus)
        )
        {
            NotePublic(entry, lastInsertUtc.Value);
            MarkEntryUploaded(entryFile, recording.Directory);
        }
        else
        {
            logger.LogInformation(
                "Replay {ReplayId} uploaded as {Privacy}. A private video is not treated as public.",
                entry.ReplayId,
                entry.ActualPrivacyStatus ?? UploadVisibility.Staged
            );
        }

        SaveLedger();
        RecordWork?.Invoke();
        MediaRetention.SweepAndLog(settings, logger);
    }

    private enum Recovery
    {
        None,
        Resume,
        Restart,
        Reschedule,
    }

    private sealed record RecoveryResult(
        UploadOutcome? Outcome,
        Recovery How,
        UploadAttemptManifest Manifest
    );

    /// <summary>
    /// An attempt whose send was interrupted (a stop, a restart, a network failure). The
    /// uploader retries it on its own a bounded number of times, and never inserts a second copy:
    /// a saved session is asked how much YouTube holds before anything is sent.
    /// </summary>
    private async Task<RecoveryResult> RecoverAsync(
        UploadOutbox outbox,
        UploadAttemptManifest manifest,
        FileInfo recording,
        FileInfo entryFile,
        YouTubeEntry entry,
        CancellationToken token
    )
    {
        string attemptId = manifest.AttemptId;
        string path = recording.FullName;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (manifest.State == UploadAttemptState.Uploading)
        {
            // The process that sent it died without recording how the send ended.
            UploadAttemptResult marked = await outbox
                .MarkAmbiguousAsync(attemptId, now, CancellationToken.None)
                .ConfigureAwait(false);
            if (!marked.Succeeded)
            {
                logger.LogWarning(
                    "Could not record the interrupted upload {Attempt} ({Reason}). {Path} stays pending.",
                    attemptId,
                    marked.Reason,
                    path
                );
                return new RecoveryResult(UploadOutcome.Failed, Recovery.None, manifest);
            }

            manifest = marked.Manifest;
        }

        int max = settings.YouTube.InterruptedUploadRetries;
        int used = retries.Count(attemptId);
        bool hasSession = UploadAttemptIds.IsSessionUri(manifest.SessionUri);
        InterruptedUploadAction action = InterruptedUpload.Plan(
            used,
            max,
            hasSession,
            OnChannel(entry)
        );
        switch (action)
        {
            case InterruptedUploadAction.Exhausted:
                LogWait(
                    path,
                    "operator-retry",
                    "Upload of {Path} was interrupted and its {Max} automatic retries are used. It stays pending for an operator: remove {Attempt} from {RetryFile} to allow more.",
                    path,
                    max,
                    attemptId,
                    InterruptedUploadRetries.FileName
                );
                return new RecoveryResult(UploadOutcome.Parked, Recovery.None, manifest);
            case InterruptedUploadAction.AlreadyOnYouTube:
                LogWait(
                    path,
                    "on-channel",
                    "Upload of {Path} was interrupted with no upload session saved, and replay {ReplayId} is already on the channel ({Catalog}). It is not sent again; it stays pending for an operator.",
                    path,
                    entry.ReplayId,
                    YouTubeReplayCatalog.FileName
                );
                return new RecoveryResult(UploadOutcome.Parked, Recovery.None, manifest);
            case InterruptedUploadAction.Restart:
                return new RecoveryResult(null, Recovery.Restart, manifest);
        }

        UploadSessionStatus status;
        if (incompleteSessions.ContainsKey(attemptId))
        {
            // Nothing was sent to it since it was found incomplete in this process.
            status = new UploadSessionStatus { State = UploadSessionState.Incomplete };
        }
        else
        {
            using YouTubeService service = await CreateServiceAsync().ConfigureAwait(false);
            status = await UploadSessionProbe
                .ProbeAsync(
                    service.HttpClient,
                    manifest.SessionUri,
                    recording.Length,
                    body => service.Serializer.Deserialize<Video>(body),
                    token
                )
                .ConfigureAwait(false);
        }

        DateTimeOffset? slotAt = YouTubeListing.IsPublic(settings.YouTube)
            ? PublicationReservation.HeldAt(ReservationsPath(false), WorkKey(entry, path))
            : null;
        switch (InterruptedUpload.AfterProbe(status.State, slotAt, now))
        {
            case InterruptedUploadAction.Confirm:
                logger.LogInformation(
                    "Upload of {Path} had finished on YouTube before its response was lost: video {VideoId}. It is recorded and not sent again.",
                    path,
                    status.Video.Id
                );
                await RecordUploadedAsync(
                        outbox,
                        attemptId,
                        status.Video,
                        recording,
                        entryFile,
                        entry,
                        reconcile: true
                    )
                    .ConfigureAwait(false);
                return new RecoveryResult(UploadOutcome.Uploaded, Recovery.None, manifest);
            case InterruptedUploadAction.SessionGone:
                LogWait(
                    path,
                    "session-gone",
                    "Upload of {Path} was interrupted and YouTube no longer knows its upload session (HTTP {Status}), so it may have finished. It is not sent again; it stays pending for an operator.",
                    path,
                    status.HttpStatus
                );
                return new RecoveryResult(UploadOutcome.Parked, Recovery.None, manifest);
            case InterruptedUploadAction.Resume:
                incompleteSessions[attemptId] = 0;
                if (status.BytesReceived > 0)
                {
                    logger.LogInformation(
                        "YouTube holds {Bytes} of {Total} bytes of the interrupted upload of {Path}, and no video yet.",
                        status.BytesReceived,
                        recording.Length,
                        path
                    );
                }

                return new RecoveryResult(null, Recovery.Resume, manifest);
            case InterruptedUploadAction.Reschedule:
                incompleteSessions[attemptId] = 0;
                logger.LogInformation(
                    "YouTube holds {Bytes} of {Total} bytes of the interrupted upload of {Path}, and no video. Its publish time {PublishAt:u} has passed, so it gets the next valid one.",
                    status.BytesReceived,
                    recording.Length,
                    path,
                    slotAt
                );
                return new RecoveryResult(null, Recovery.Reschedule, manifest);
            default:
                LogWait(
                    path,
                    "session-check-failed",
                    "Could not ask YouTube about the interrupted upload of {Path} ({Detail}). Nothing is sent; the next pass asks again.",
                    path,
                    status.Detail
                );
                return new RecoveryResult(UploadOutcome.Failed, Recovery.None, manifest);
        }
    }

    private static string DescribeRecovery(
        Recovery recovery,
        UploadAttemptManifest manifest,
        DateTimeOffset? publishAt
    )
    {
        return recovery switch
        {
            Recovery.Resume => "resuming its upload session",
            Recovery.Restart =>
                "a new upload (no session was saved, so YouTube never got the file)",
            Recovery.Reschedule => "a new upload at the new publish time "
                + publishAt?.ToString("u", System.Globalization.CultureInfo.InvariantCulture)
                + " (the old session had no video and is dropped)",
            _ => "a new upload of " + manifest?.AttemptId,
        };
    }

    /// <summary>The open attempt for <paramref name="recordingPath"/> whose send was interrupted.</summary>
    private async Task<UploadAttemptManifest> FindInterruptedAsync(
        UploadOutbox outbox,
        string recordingPath,
        CancellationToken token
    )
    {
        Dictionary<string, UploadAttemptManifest> known = interruptedInPass;
        if (known == null)
        {
            known = Interrupted(await outbox.ListOpenAsync(token).ConfigureAwait(false));
        }

        return known.TryGetValue(Path.GetFullPath(recordingPath), out UploadAttemptManifest found)
            ? found
            : null;
    }

    private static Dictionary<string, UploadAttemptManifest> Interrupted(
        IReadOnlyList<UploadAttemptManifest> open
    )
    {
        var found = new Dictionary<string, UploadAttemptManifest>(StringComparer.OrdinalIgnoreCase);
        foreach (UploadAttemptManifest manifest in open ?? Array.Empty<UploadAttemptManifest>())
        {
            if (
                manifest == null
                || string.IsNullOrWhiteSpace(manifest.MediaPath)
                || (
                    manifest.State != UploadAttemptState.Uploading
                    && manifest.State != UploadAttemptState.AmbiguousUpload
                )
            )
            {
                continue;
            }

            try
            {
                found[Path.GetFullPath(manifest.MediaPath)] = manifest;
            }
            catch (Exception exception)
                when (exception is ArgumentException or NotSupportedException)
            {
                // A path that cannot be a recording is not one.
            }
        }

        return found;
    }

    private bool OnChannel(YouTubeEntry entry) =>
        entry?.ReplayId is int id
        && id > 0
        && YouTubeReplayCatalog.Contains(
            YouTubeReplayCatalog.PathFor(settings.Location?.DataDirectory),
            id
        );

    /// <summary>
    /// The watcher sees an mp4 when OBS creates it, and the spectator writes the entry only after
    /// the recording stops, any clips are cut, and the publication decision is made. A recording
    /// still inside <c>Retention:UnpublishedGrace</c> is not an error: the pending pass sends it
    /// once the entry exists. An older one never got an entry, and retention removes it.
    /// </summary>
    private UploadOutcome LogMissingEntry(FileInfo recording)
    {
        TimeSpan grace =
            settings.Retention?.UnpublishedGrace > TimeSpan.Zero
                ? settings.Retention.UnpublishedGrace
                : TimeSpan.FromHours(1);
        recording.Refresh();
        if (!recording.Exists || DateTime.UtcNow - recording.LastWriteTimeUtc < grace)
        {
            LogWait(
                recording.FullName,
                "no-entry-yet",
                "No {Entry} next to {Path} yet. The pending pass sends it once the spectator writes the entry.",
                settings.YouTube.EntryFileName,
                recording.FullName
            );
            return UploadOutcome.Waiting;
        }

        LogWait(
            recording.FullName,
            "no-entry",
            "No {Entry} next to {Path} {Quiet} after the recording stopped. It is not uploaded, and retention removes it.",
            settings.YouTube.EntryFileName,
            recording.FullName,
            grace
        );
        return UploadOutcome.Done;
    }

    private async Task<bool> ReturnUnsentAsync(UploadOutbox outbox, string attemptId)
    {
        try
        {
            UploadAttemptResult returned = await outbox
                .ReturnUnsentAsync(attemptId, DateTimeOffset.UtcNow, CancellationToken.None)
                .ConfigureAwait(false);
            return returned.Succeeded;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not return the unsent upload {Attempt}.", attemptId);
            return false;
        }
    }

    private async Task RecordInterruptedSendAsync(UploadOutbox outbox, string attemptId)
    {
        try
        {
            UploadAttemptResult marked = await outbox
                .MarkAmbiguousAsync(attemptId, DateTimeOffset.UtcNow, CancellationToken.None)
                .ConfigureAwait(false);
            if (!marked.Succeeded)
            {
                logger.LogWarning(
                    "Could not record the interrupted upload {Attempt} ({Reason}).",
                    attemptId,
                    marked.Reason
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record the interrupted upload {Attempt}.", attemptId);
        }
    }

    /// <summary>
    /// One upload pass over every pending recording. A pass where each recording was sent or
    /// correctly waits (quota, the insert cap, a publish slot, its entry) is healthy work for
    /// <c>services status</c>, and so is a pass with nothing pending. A failed or parked
    /// recording makes the pass unhealthy until a later pass is clean.
    /// </summary>
    internal async Task RunUploadPassAsync()
    {
        quotaHeld = false;
        IReadOnlyList<string> pending = PendingYouTubeUpload.RequestsFirst(
            PendingYouTubeUpload.Find(
                settings.ContextsDirectory,
                settings.YouTube.EntryFileName,
                settings.YouTube.EntryFileNameUploaded,
                PendingYouTubeUpload.AttemptsDirectory(settings.Location?.DataDirectory)
            ),
            settings.YouTube.EntryFileName
        );
        waits.Retain(pending);
        bool healthy = true;
        int sentNow = 0;
        if (pending.Count > 0)
        {
            bool liveSend = settings.YouTube.Enabled != false && !settings.YouTube.DryRun;
            CancellationToken token = cancellationTokenSource.Token;
            var outbox = new UploadOutbox(MediaPolicyAttemptLog.AttemptsRoot(settings));
            interruptedInPass = liveSend
                ? Interrupted(await outbox.ListOpenAsync(token).ConfigureAwait(false))
                : new Dictionary<string, UploadAttemptManifest>(StringComparer.OrdinalIgnoreCase);
            (string gate, DateTimeOffset? resumeAt) = liveSend
                ? PassGate(DateTimeOffset.UtcNow)
                : (null, null);
            LogPassGate(gate, resumeAt, pending.Count);
            try
            {
                foreach (string path in pending)
                {
                    // A closed gate holds every new insert. Only an interrupted upload goes on: a
                    // resumed session spends no new videos.insert call.
                    if (gate != null && !interruptedInPass.ContainsKey(Path.GetFullPath(path)))
                    {
                        waits.Wait(path, gate);
                        continue;
                    }

                    UploadOutcome outcome;
                    try
                    {
                        outcome = await ProcessOnceAsync(path).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogInformation(
                            "Upload pass stopped for the service stop at {Path}. The next start retries it automatically.",
                            path
                        );
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Could not upload {Path}", path);
                        outcome = UploadOutcome.Failed;
                    }

                    if (outcome == UploadOutcome.Uploaded)
                    {
                        sentNow++;
                    }

                    if (outcome is UploadOutcome.Failed or UploadOutcome.Parked)
                    {
                        healthy = false;
                    }
                }
            }
            finally
            {
                interruptedInPass = null;
            }

            LogPublicationHealth(pending.Count, pending, sentNow > 0);
        }

        lastPassHealthy = healthy;
        if (healthy)
        {
            RecordWork?.Invoke();
        }

        ReportHealth();
    }

    /// <summary>
    /// A limit that holds every new insert this pass: the upload bucket (or a quota pause), or
    /// this app's daily insert cap. Null when uploads may start.
    /// </summary>
    private (string Gate, DateTimeOffset? ResumeAt) PassGate(DateTimeOffset now)
    {
        try
        {
            YouTubeQuotaDay day = quotaUnits.Read(now);
            if (!quotaUnits.MayUpload(day, now))
            {
                quotaHeld = true;
                return (UploadBucket, quotaUnits.UploadsResumeAt(day, now));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not read the YouTube quota units. The upload goes on.");
            return (null, null);
        }

        if (InsertCapped(now))
        {
            return (PublicationSchedule.InsertCap, YouTubeQuotaUnits.NextQuotaDay(now));
        }

        return (null, null);
    }

    private bool InsertCapped(DateTimeOffset now)
    {
        EnsureLedger();
        ReplayMediaPolicySettings media = settings.ReplayMedia ?? new ReplayMediaPolicySettings();
        return PublicationSchedule.QuotaDayStart(now) == currentQuotaDay
            && insertsToday >= media.MaxInsertsPerQuotaDay;
    }

    /// <summary>One line when a gate closes or changes, not one per recording per pass.</summary>
    private void LogPassGate(string gate, DateTimeOffset? resumeAt, int pending)
    {
        if (string.Equals(gate, passGate, StringComparison.Ordinal))
        {
            return;
        }

        passGate = gate;
        if (gate == PublicationSchedule.InsertCap)
        {
            logger.LogInformation(
                "{Pending} recording(s) wait: the daily insert cap is reached ({Inserts} of ReplayMedia:MaxInsertsPerQuotaDay {Cap} videos.insert calls this Pacific quota day). This is this app's cap, not YouTube's upload bucket. Uploads resume at {ResumeAt:u}.",
                pending,
                insertsToday,
                (settings.ReplayMedia ?? new ReplayMediaPolicySettings()).MaxInsertsPerQuotaDay,
                resumeAt
            );
        }
        else if (gate == UploadBucket)
        {
            logger.LogInformation(
                "{Pending} recording(s) wait for the YouTube upload bucket (or a quota pause). Uploads resume at {ResumeAt:u}.",
                pending,
                resumeAt
            );
        }
        else
        {
            logger.LogInformation("New uploads may start again.");
        }
    }

    private void LogWait(string path, string reason, string message, params object[] args)
    {
        if (waits.Wait(path, reason))
        {
            logger.LogInformation(message, args);
        }
    }

    /// <summary>
    /// The YouTube quota is the only limit that keeps a recording on disk. The budget only
    /// moves its publish time. An ordinary recording leaves the day's last inserts to viewer
    /// requests that are still waiting (#161).
    /// </summary>
    private bool QuotaHasRoom(string path, bool requested)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        YouTubeQuotaDay day;
        try
        {
            day = quotaUnits.Read(now);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not read the YouTube quota units. The upload goes on.");
            return true;
        }

        if (!quotaUnits.MayUpload(day, now))
        {
            quotaHeld = true;
            LogWait(
                path,
                UploadBucket,
                "Upload of {Path} waits for the YouTube upload quota ({Calls} of {Usable} videos.insert calls used today, {Reserve} held back). Uploads resume at {ResumeAt:u}. It stays pending.",
                path,
                day.UploadCalls,
                YouTubeQuotaUnits.UsableUploadCalls(settings.YouTube),
                settings.YouTube.UploadCallReserve,
                quotaUnits.UploadsResumeAt(day, now)
            );
            return false;
        }

        if (requested)
        {
            return true;
        }

        int requestsWaiting = RequestsWaiting(path);
        if (quotaUnits.MayUpload(day, now, requested: false, requestsWaiting))
        {
            return true;
        }

        quotaHeld = true;
        LogWait(
            path,
            "requests-first",
            "Upload of {Path} waits: {Left} insert(s) are left today and {Requests} viewer request(s) go first. Uploads resume at {ResumeAt:u}. It stays pending.",
            path,
            quotaUnits.InsertsLeft(day),
            requestsWaiting,
            YouTubeQuotaUnits.NextQuotaDay(now)
        );
        return false;
    }

    private int RequestsWaiting(string excluding)
    {
        try
        {
            return PendingYouTubeUpload.CountRequestsWaiting(
                settings.ContextsDirectory,
                settings.YouTube.EntryFileName,
                settings.YouTube.EntryFileNameUploaded,
                excluding
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not count the viewer requests waiting for YouTube.");
            return 0;
        }
    }

    /// <summary>
    /// A daily quota response to <c>videos.insert</c> is the upload bucket: it pauses uploads
    /// until the quota day turns. The library pass spends the other bucket and goes on. A rate
    /// limit only holds new uploads for one cycle. Returns when uploads may go again, or null.
    /// </summary>
    private DateTimeOffset? PauseOnQuota(Exception exception)
    {
        YouTubeQuotaRefusal refusal = YouTubeListQuota.Classify(exception);
        if (refusal == YouTubeQuotaRefusal.None)
        {
            return null;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (refusal == YouTubeQuotaRefusal.RateLimited)
        {
            DateTimeOffset retry = now + YouTubeListQuota.RateLimitWait;
            Bookkeep(() => quotaUnits.PauseUploads(retry, now));
            logger.LogWarning(
                exception,
                "YouTube rate-limited an upload. The day's quota is not spent. New uploads wait until {Until}.",
                retry
            );
            return retry;
        }

        DateTimeOffset until = YouTubeListQuota.ResumeAt(now);
        Bookkeep(() => quotaUnits.PauseUploads(until, now));
        logger.LogWarning(
            exception,
            "YouTube refused videos.insert for the day (the Video Uploads bucket). New uploads wait until {Until}. The library pass uses the other bucket and goes on.",
            until
        );
        return until;
    }

    /// <summary>
    /// Reserves the replay's publish time. A dry run plans in its own ledger, so its times do
    /// not take a live slot, and it never deletes a recording. A held slot whose time is before
    /// <paramref name="rescheduleIfBefore"/> gets the next valid time instead of a past one.
    /// </summary>
    private async Task<PublicationReservationResult> ReservePublicationSlot(
        YouTubeEntry entry,
        string path,
        bool dryRun,
        DateTimeOffset? rescheduleIfBefore = null
    )
    {
        EnsureLedger();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset quotaDay = PublicationSchedule.QuotaDayStart(now);
        if (currentQuotaDay != quotaDay)
        {
            currentQuotaDay = quotaDay;
            insertsToday = 0;
        }

        PublicationSendFacts facts = await ReadSendFactsAsync(entry).ConfigureAwait(false);
        bool publicListing = YouTubeListing.IsPublic(settings.YouTube);
        PublicationReservationResult reserved = PublicationReservation.TryReserve(
            ReservationsPath(dryRun),
            publicListing,
            insertsToday,
            now,
            lastPublicUtc,
            publicAtUtc,
            RequestedInDay(now),
            facts.Criteria == ReplayMediaPriority.Requested,
            facts.RecordedAtUtc,
            entry?.Map,
            lastMap,
            lastMapUtc,
            entry?.Hero,
            lastHero,
            lastHeroUtc,
            WorkKey(entry, path),
            settings.ReplayMedia,
            facts,
            entry?.Rank,
            entry?.Heroes,
            rescheduleIfBefore
        );
        if (dryRun)
        {
            logger.LogInformation(
                "Dry run plan for {Path}: {Kind} ({Reason}), publish at {PublishAt}.",
                path,
                reserved.Kind,
                reserved.Reason,
                reserved.PublishAtUtc
            );
            return reserved;
        }

        if (reserved.Kind == PublicationReservation.Terminal)
        {
            waits.Clear(path);
            logger.LogWarning(
                "Removed recording that was eligible but never uploaded: {Path}. It is past the publication window ({Reason}: the game is older than ReplayMedia:OrdinaryCandidateMaxAge {MaxAge}) and is not reserved again. Recording more than can be published causes this; see the recording cap.",
                path,
                reserved.Reason,
                settings.ReplayMedia?.OrdinaryCandidateMaxAge
            );
            DeleteRecording(path);
            return reserved;
        }

        if (reserved.Allow)
        {
            waits.Clear(path);
            if (reserved.RescheduledFrom is DateTimeOffset from)
            {
                logger.LogInformation(
                    "Replay {ReplayId} missed its publish time {From:u} before its upload finished. It now publishes at {PublishAt:u} ({Reason}).",
                    entry?.ReplayId,
                    from,
                    reserved.PublishAtUtc,
                    reserved.Reason
                );
            }
            else
            {
                logger.LogInformation(
                    "Replay {ReplayId} publishes at {PublishAt} ({Reason}). It is sent now.",
                    entry?.ReplayId,
                    reserved.PublishAtUtc,
                    reserved.Reason
                );
            }

            return reserved;
        }

        LogRefusedSlot(path, reserved);
        return reserved;
    }

    private void LogRefusedSlot(string path, PublicationReservationResult reserved)
    {
        ReplayMediaPolicySettings media = settings.ReplayMedia ?? new ReplayMediaPolicySettings();
        switch (reserved.Reason)
        {
            case PublicationSchedule.PublicationFull:
                LogWait(
                    path,
                    reserved.Reason,
                    "Upload of {Path} has no publish time within ReplayMedia:MaxPublishAhead {Ahead} (publication-full: every time in that window breaks a pacing rule for this replay). It stays pending and is tried again each pass.",
                    path,
                    media.MaxPublishAhead
                );
                break;
            case PublicationSchedule.InsertCap:
                LogWait(
                    path,
                    reserved.Reason,
                    "Upload of {Path} waits for the daily insert cap ({Inserts} of ReplayMedia:MaxInsertsPerQuotaDay {Cap} this Pacific quota day, not YouTube's upload bucket). Uploads resume at {ResumeAt:u}. It stays pending.",
                    path,
                    insertsToday,
                    media.MaxInsertsPerQuotaDay,
                    YouTubeQuotaUnits.NextQuotaDay(DateTimeOffset.UtcNow)
                );
                break;
            default:
                LogWait(
                    path,
                    reserved.Reason,
                    "Upload of {Path} is not sent ({Reason}). It stays pending.",
                    path,
                    reserved.Reason
                );
                break;
        }
    }

    private string ReservationsPath(bool dryRun) =>
        settings.Location?.DataDirectory == null
            ? null
            : PublicationReservation.PathFor(settings.Location.DataDirectory, dryRun);

    private static string WorkKey(YouTubeEntry entry, string path)
    {
        if (entry?.ReplayId is > 0)
        {
            return "replay-"
                + entry.ReplayId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        string name = Path.GetFileName(path) ?? "recording";
        var key = new System.Text.StringBuilder("file-");
        foreach (char character in name)
        {
            bool allowed =
                (character >= 'a' && character <= 'z')
                || (character >= 'A' && character <= 'Z')
                || (character >= '0' && character <= '9');
            if (allowed)
            {
                key.Append(character);
            }
        }

        if (key.Length == "file-".Length)
        {
            key.Append("recording");
        }

        return key.Length <= 80 ? key.ToString() : key.ToString(0, 80);
    }

    private async Task<PublicationSendFacts> ReadSendFactsAsync(YouTubeEntry entry)
    {
        DateTimeOffset? recorded = entry?.RecordedAtUtc;
        if (entry?.ReplayId is not int replayId || replayId <= 0)
        {
            return PublicationSendFacts.Unverified(recorded);
        }

        if (string.IsNullOrWhiteSpace(settings.Location?.DataDirectory))
        {
            return PublicationSendFacts.Unverified(recorded);
        }

        var outbox = new UploadOutbox(MediaPolicyAttemptLog.AttemptsRoot(settings));
        UploadAttemptResult loaded = await outbox
            .LoadAsync(
                "replay-" + replayId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CancellationToken.None
            )
            .ConfigureAwait(false);
        if (!loaded.Succeeded)
        {
            return PublicationSendFacts.Unverified(recorded);
        }

        return PublicationSendFacts.FromDecision(
            MediaPolicyManifest.ToDecision(loaded.Manifest),
            recorded
        );
    }

    /// <summary>
    /// Published and stuck counts come from the library record, which the library pass updates
    /// when YouTube reports a scheduled video public. The uploader's own ledger only sees the
    /// insert response, which is always private with a publish time (#250).
    /// </summary>
    private PublicationTallyReport Tally(DateTimeOffset now)
    {
        try
        {
            return PublicationTally.Count(
                YouTubeLibraryRecord
                    .Read(YouTubeLibraryRecord.PathFor(settings.Location?.DataDirectory))
                    .Values,
                now,
                PublicationTally.ConfirmGrace(settings.YouTube.LibraryInterval)
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not read the YouTube library record.");
            return new PublicationTallyReport();
        }
    }

    private void LogQuotaPlan()
    {
        logger.LogInformation(
            "{QuotaPlan}",
            YouTubeQuotaPlan.Describe(settings.YouTube, settings.ReplayMedia)
        );
        foreach (
            string warning in YouTubeQuotaPlan.Warnings(settings.YouTube, settings.ReplayMedia)
        )
        {
            logger.LogWarning("{QuotaPlanWarning}", warning);
        }
    }

    /// <summary>
    /// Tells <c>services status</c> whether the uploader is degraded: blocked by quota or the
    /// insert cap with a backlog, or nothing confirmed public for a day while uploads are past
    /// their publish time.
    /// </summary>
    private void ReportHealth()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        YouTubeUploaderConcern concern;
        try
        {
            YouTubeQuotaDay day = quotaUnits.Read(now);
            ReplayMediaPolicySettings media =
                settings.ReplayMedia ?? new ReplayMediaPolicySettings();
            bool insertsCapped = InsertCapped(now);
            bool bucketBlocked = !quotaUnits.MayUpload(day, now);
            concern = YouTubeUploaderHealth.Evaluate(
                new YouTubeUploaderHealthInput
                {
                    Live = settings.YouTube.Enabled && !settings.YouTube.DryRun,
                    PublicListing = YouTubeListing.IsPublic(settings.YouTube),
                    Pending = PendingUploadSize.Count(
                        settings.ContextsDirectory,
                        settings.YouTube.EntryFileName,
                        settings.YouTube.EntryFileNameUploaded
                    ),
                    QuotaBlocked = bucketBlocked,
                    InsertCapped = insertsCapped,
                    InsertCap = media.MaxInsertsPerQuotaDay,
                    UploadsResumeAt =
                        quotaUnits.UploadsResumeAt(day, now)
                        ?? (insertsCapped ? YouTubeQuotaUnits.NextQuotaDay(now) : null),
                    Tally = Tally(now),
                    LibraryProblem = libraryProblem,
                    Now = now,
                }
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not check the uploader's health.");
            return;
        }

        ServiceHost.ServiceHeartbeat.RecordConcern(concern?.Code, concern?.Cause);
        if (concern?.Code == reportedConcern)
        {
            return;
        }

        if (concern == null)
        {
            logger.LogInformation(
                "YouTube uploader is healthy again ({Previous} cleared).",
                reportedConcern
            );
        }
        else
        {
            logger.LogWarning(
                "YouTube uploader is degraded ({Code}): {Cause}",
                concern.Code,
                concern.Cause
            );
        }

        reportedConcern = concern?.Code;
    }

    /// <summary>
    /// Writes <c>publication-status.txt</c> after every pass and logs the summary line at most
    /// every <see cref="UploadWaitLog.SummaryInterval"/>, or at once after a pass that sent.
    /// </summary>
    private void LogPublicationHealth(
        int pending,
        IReadOnlyList<string> candidates,
        bool sentThisPass
    )
    {
        EnsureLedger();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ReplayMediaPolicySettings media = settings.ReplayMedia ?? new ReplayMediaPolicySettings();
        PublicationTallyReport tally = Tally(now);
        string limit = passGate ?? waits.Main();
        PublicationHealthReport health = PublicationHealth.Summarize(
            pending,
            insertsToday,
            waits.Count,
            quotaHeld || InsertCapped(now),
            "1",
            tally.PublishedDay,
            tally.PublishedWeek,
            tally.StuckPrivate,
            tally.Scheduled,
            limit
        );
        if (waits.SummaryDue(now) || sentThisPass)
        {
            logger.LogInformation(
                "YouTube publication health pending {Pending} uploaded {Uploaded} deferred {Deferred} published-day {PublishedDay} published-week {PublishedWeek} scheduled {Scheduled} stuck-private {StuckPrivate} limit {Limit} policy {Policy}. Waiting: {Waiting}.",
                health.Pending,
                health.Uploaded,
                health.Deferred,
                health.PublishedDay,
                health.PublishedWeek,
                health.Scheduled,
                health.StuckPrivate,
                health.Limit,
                health.PolicyVersion,
                waits.Describe()
            );
        }

        if (string.IsNullOrWhiteSpace(settings.Location?.DataDirectory))
        {
            return;
        }

        int reserved = media.ReservedRequestSlotsPerDay - RequestedInDay(now);
        if (reserved < 0)
        {
            reserved = 0;
        }

        DateTimeOffset? latest = PublicationReservation.Latest(ReservationsPath(dryRun: false));
        if (lastPublicUtc != null && (latest == null || lastPublicUtc.Value > latest.Value))
        {
            latest = lastPublicUtc;
        }

        DateTimeOffset next = latest == null ? now : latest.Value.Add(media.MinimumPublicInterval);
        if (next < now)
        {
            next = now;
        }

        PublicationHealth.WriteStatus(
            Path.Combine(settings.Location.DataDirectory, "publication-status.txt"),
            health,
            candidates,
            reserved,
            next
        );
    }

    private async Task NoteSessionIfPresentAsync(
        UploadOutbox outbox,
        string attemptId,
        string sessionUri
    )
    {
        if (!UploadAttemptIds.IsSessionUri(sessionUri))
        {
            return;
        }

        try
        {
            UploadAttemptResult noted = await outbox
                .NoteSessionAsync(
                    attemptId,
                    sessionUri,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            if (!noted.Succeeded)
            {
                logger.LogWarning(
                    "Could not record the resumable session for {Attempt} ({Reason}).",
                    attemptId,
                    noted.Reason
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not record the resumable session for {Attempt}.",
                attemptId
            );
        }
    }

    private async Task<UploadOutcome> ProcessOnceAsync(string recordingPath)
    {
        if (string.IsNullOrWhiteSpace(recordingPath))
        {
            return UploadOutcome.Done;
        }

        string key = Path.GetFullPath(recordingPath);
        if (!uploadsInFlight.TryAdd(key, 0))
        {
            logger.LogInformation("Upload of {Path} is already in progress.", key);
            return UploadOutcome.Waiting;
        }

        try
        {
            return await ProcessRecordingAsync(key).ConfigureAwait(false);
        }
        finally
        {
            uploadsInFlight.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// The uploader's library pass: a short delay after start, then every
    /// <c>YouTube:LibraryInterval</c>, independent of the upload passes, so a backlog of
    /// recordings never holds it back.
    /// </summary>
    private async Task RunLibraryLoopAsync(CancellationToken token)
    {
        if (library == null)
        {
            return;
        }

        TimeSpan delay =
            settings.YouTube.LibraryStartupDelay >= TimeSpan.Zero
                ? settings.YouTube.LibraryStartupDelay
                : TimeSpan.FromMinutes(2);
        try
        {
            await Task.Delay(delay, token).ConfigureAwait(false);
            bool startup = true;
            while (!token.IsCancellationRequested)
            {
                YouTubeLibraryPass pass = await FileLibraryAsync(startup, token)
                    .ConfigureAwait(false);
                startup = false;
                await Task.Delay(
                        YouTubeLibrary.NextPassIn(
                            pass,
                            settings.YouTube.LibraryInterval,
                            DateTimeOffset.UtcNow
                        ),
                        token
                    )
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // shut down
        }
    }

    /// <summary>
    /// One library pass. A pass that ran, or a routine skip (not due yet, another process has
    /// it, the day's units are spent, a quota pause), is healthy work. A pass that could not run
    /// for want of the library consent is not, and the publishing concern names it.
    /// </summary>
    internal async Task<YouTubeLibraryPass> FileLibraryAsync(bool startup, CancellationToken token)
    {
        if (library == null)
        {
            return null;
        }

        try
        {
            YouTubeLibraryPass pass = await library
                .RunInBackgroundAsync(startup, token)
                .ConfigureAwait(false);
            if (pass?.SkipCode is "no-consent" or "no-data-directory")
            {
                libraryProblem = pass.Skipped;
            }
            else
            {
                libraryProblem = null;
                RecordWork?.Invoke();
            }

            return pass;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "YouTube library pass did not finish. Uploads continue.");
            return null;
        }
    }

    private async Task<YouTubeService> CreateServiceAsync()
    {
        UserCredential credential = await AuthorizeAsync().ConfigureAwait(false);
        return new YouTubeService(
            new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApiKey = settings.YouTube.ApiKey,
                ApplicationName = Assembly.GetExecutingAssembly().GetName().Name,
            }
        );
    }

    private async Task<UserCredential> AuthorizeAsync()
    {
        string secretsPath = Path.Combine(settings.Location.DataDirectory, "client_secrets.json");
        if (!File.Exists(secretsPath))
        {
            throw new FileNotFoundException(
                "Google OAuth client_secrets.json is missing. Run tools/fill-secrets-from-op.ps1.",
                secretsPath
            );
        }

        await using FileStream stream = new(secretsPath, FileMode.Open, FileAccess.Read);
        UserCredential credential = await GoogleWebAuthorizationBroker
            .AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                new[] { YouTubeService.Scope.YoutubeUpload },
                settings.YouTube.ChannelId,
                cancellationTokenSource.Token
            )
            .ConfigureAwait(false);
        if (!oauthLogged)
        {
            oauthLogged = true;
            logger.LogInformation(
                "YouTube OAuth ready for channel {ChannelId}.",
                settings.YouTube.ChannelId
            );
        }

        return credential;
    }

    /// <summary>
    /// A recording found by the pending pass already has its entry, which the spectator writes
    /// only after the recording stopped, so it is ready at once: the pass does not watch a
    /// finished multi-gigabyte file for 10 seconds every time. A new mp4 the watcher sees waits
    /// until its size holds still for <c>ReadyStableReads</c> reads.
    /// </summary>
    private async Task<FileInfo> WaitForRecordingReadyAsync(
        string recordingPath,
        CancellationToken token
    )
    {
        var finished = new FileInfo(recordingPath);
        if (
            finished.Exists
            && finished.Length > 0
            && !string.IsNullOrWhiteSpace(settings.YouTube.EntryFileName)
            && File.Exists(Path.Combine(finished.DirectoryName, settings.YouTube.EntryFileName))
            && DateTime.UtcNow - finished.LastWriteTimeUtc > TimeSpan.FromSeconds(30)
            && TryOpenRead(finished.FullName)
        )
        {
            return finished;
        }

        long lastLength = -1;
        int stable = 0;
        while (!token.IsCancellationRequested)
        {
            var recording = new FileInfo(recordingPath);
            recording.Refresh();
            if (!recording.Exists && lastLength >= 0)
            {
                // The spectator discarded it (not published), or retention removed it.
                logger.LogInformation(
                    "Recording {Path} was removed before it was ready. Nothing to upload.",
                    recordingPath
                );
                return null;
            }

            if (recording.Exists && recording.Length > 0 && recording.Length == lastLength)
            {
                if (TryOpenRead(recording.FullName))
                {
                    stable++;
                    int need =
                        settings.YouTube.ReadyStableReads > 0
                            ? settings.YouTube.ReadyStableReads
                            : 5;
                    if (stable >= need)
                    {
                        logger.LogInformation(
                            "Recording ready {Path} ({Bytes} bytes).",
                            recording.FullName,
                            recording.Length
                        );
                        return recording;
                    }
                }
                else
                {
                    stable = 0;
                }
            }
            else
            {
                stable = 0;
            }

            lastLength = recording.Exists ? recording.Length : -1;
            int poll =
                settings.YouTube.ReadyPollMilliseconds > 0
                    ? settings.YouTube.ReadyPollMilliseconds
                    : 2000;
            await Task.Delay(TimeSpan.FromMilliseconds(poll), token).ConfigureAwait(false);
        }

        token.ThrowIfCancellationRequested();
        return new FileInfo(recordingPath);
    }

    private async Task<FileInfo> WaitForEntryAsync(DirectoryInfo directory, CancellationToken token)
    {
        for (int i = 0; i < 30; i++)
        {
            FileInfo entryFile = directory
                .GetFiles("*.json", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(file =>
                    file.Name.Equals(
                        settings.YouTube.EntryFileName,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            if (entryFile != null && entryFile.Exists)
            {
                return entryFile;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
        }

        return null;
    }

    private static bool TryOpenRead(string path)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite
            );
            return stream.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The receipt is the plan a live send would follow: the insert settings, and the publish
    /// time or the reason the recording would wait.
    /// </summary>
    private async Task CompleteDryRunAsync(
        FileInfo recording,
        YouTubeEntry entry,
        PublicationReservationResult slot,
        CancellationToken token
    )
    {
        string receiptPath = Path.Combine(recording.Directory.FullName, "youtube-dry-run.json");
        Video video = UploadBody.Build(entry);
        string receipt = JsonSerializer.Serialize(
            new
            {
                entry.Title,
                entry.PrivacyStatus,
                entry.DesiredPrivacyStatus,
                PublishAtUtc = video.Status.PublishAtDateTimeOffset,
                Schedule = slot?.Kind,
                ScheduleReason = slot?.Reason,
                video.Status.SelfDeclaredMadeForKids,
                video.Snippet.CategoryId,
                Bytes = recording.Length,
                Recording = recording.Name,
                Simulated = true,
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        await File.WriteAllTextAsync(receiptPath, receipt, token).ConfigureAwait(false);
        logger.LogInformation(
            "Dry run saved {Receipt} for {Title} ({Bytes} bytes). YouTube was not called.",
            receiptPath,
            entry.Title,
            recording.Length
        );
        waits.Clear(recording.FullName);
        RecordWork?.Invoke();
        MediaRetention.SweepAndLog(settings, logger);
    }

    private void RememberUploaded(YouTubeEntry entry)
    {
        int? replayId = YouTubeReplayMatch.FromEntry(entry);
        if (replayId is not > 0)
        {
            return;
        }

        YouTubeReplayCatalog.Remember(
            YouTubeReplayCatalog.PathFor(settings.Location?.DataDirectory),
            replayId.Value
        );
        logger.LogInformation(
            "Recorded YouTube upload for replay {ReplayId}. Later spectates will not record it again.",
            replayId.Value
        );
    }

    /// <summary>
    /// The quota units and the library record never stop an upload. A file that stays
    /// locked is logged and the upload goes on.
    /// </summary>
    private void Bookkeep(Action write)
    {
        try
        {
            write();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not update the YouTube quota units or library record.");
        }
    }

    private void DeleteRecording(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not delete recording {Path}.", path);
        }
    }

    private void MarkEntryUploaded(FileInfo entryFile, DirectoryInfo directory)
    {
        string uploadedName = settings.YouTube.EntryFileNameUploaded;
        if (string.IsNullOrWhiteSpace(uploadedName))
        {
            return;
        }

        string uploadedPath = Path.Combine(directory.FullName, uploadedName);
        File.Move(entryFile.FullName, uploadedPath, overwrite: true);
    }

    private void EnsureLedger()
    {
        if (ledgerLoaded)
        {
            return;
        }

        ledgerLoaded = true;
        PublicationLedger saved = PublicationLedgerStore.Load(settings.Location?.DataDirectory);
        if (saved == null)
        {
            return;
        }

        insertsToday = saved.InsertsThisQuotaDay < 0 ? 0 : saved.InsertsThisQuotaDay;
        lastInsertUtc = saved.LastInsertUtc;
        if (saved.QuotaDay != default)
        {
            currentQuotaDay = saved.QuotaDay;
        }

        lastPublicUtc = saved.LastPublicUtc;
        lastMap = saved.LastMap;
        lastMapUtc = saved.LastMapUtc;
        lastHero = saved.LastHero;
        lastHeroUtc = saved.LastHeroUtc;
        if (saved.PublicAtUtc != null)
        {
            publicAtUtc.AddRange(saved.PublicAtUtc);
        }

        if (saved.Requested != null)
        {
            publicRequested.AddRange(saved.Requested);
        }

        while (publicRequested.Count < publicAtUtc.Count)
        {
            publicRequested.Add(false);
        }

        while (publicRequested.Count > publicAtUtc.Count)
        {
            publicRequested.RemoveAt(publicRequested.Count - 1);
        }
    }

    private void SaveLedger()
    {
        PublicationLedgerStore.Save(
            settings.Location?.DataDirectory,
            new PublicationLedger
            {
                PublicAtUtc = new List<DateTimeOffset>(publicAtUtc),
                Requested = new List<bool>(publicRequested),
                LastMap = lastMap,
                LastMapUtc = lastMapUtc,
                LastHero = lastHero,
                LastHeroUtc = lastHeroUtc,
                LastPublicUtc = lastPublicUtc,
                InsertsThisQuotaDay = insertsToday,
                QuotaDay = currentQuotaDay,
                LastInsertUtc = lastInsertUtc,
            }
        );
    }

    private void NotePublic(YouTubeEntry entry, DateTimeOffset now)
    {
        publicAtUtc.Add(now);
        publicRequested.Add(entry?.Requested == true);
        while (publicAtUtc.Count > 0 && now - publicAtUtc[0] >= TimeSpan.FromDays(7))
        {
            publicAtUtc.RemoveAt(0);
            publicRequested.RemoveAt(0);
        }

        lastPublicUtc = now;
        lastMap = entry?.Map;
        lastMapUtc = now;
        lastHero = entry?.Hero;
        lastHeroUtc = now;
    }

    private int RequestedInDay(DateTimeOffset now)
    {
        int count = 0;
        for (int i = 0; i < publicAtUtc.Count && i < publicRequested.Count; i++)
        {
            if (
                publicRequested[i]
                && publicAtUtc[i] <= now
                && now - publicAtUtc[i] < TimeSpan.FromHours(24)
            )
            {
                count++;
            }
        }

        return count;
    }
}
