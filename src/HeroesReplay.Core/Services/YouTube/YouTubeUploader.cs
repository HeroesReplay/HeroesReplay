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
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Media;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.Core.Services.Retention;
using HeroesReplay.Core.Services.YouTube.Outbox;
using Microsoft.Extensions.Logging;

/// <summary>
/// https://developers.google.com/youtube/v3/guides/moving_to_oauth#offlinelong-lived-access-to-the-youtube-api
/// https://developers.google.com/youtube/v3/code_samples/dotnet#upload_a_video
/// </summary>
namespace HeroesReplay.Core.Services.YouTube;

public class YouTubeUploader : IYouTubeUploader
{
    private readonly ILogger<YouTubeUploader> logger;
    private readonly AppSettings settings;
    private readonly CancellationTokenSource cancellationTokenSource;
    private readonly ConcurrentDictionary<string, byte> uploadsInFlight = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly IYouTubeLibrary library;
    private readonly List<DateTimeOffset> publicAtUtc = new();
    private readonly List<bool> publicRequested = new();
    private int insertsToday;
    private int deferredBySchedule;
    private int stuckPrivate;
    private DateTimeOffset? lastInsertUtc;
    private DateTimeOffset? lastPublicUtc;
    private DateTimeOffset currentQuotaDay;
    private string lastMap;
    private DateTimeOffset? lastMapUtc;
    private string lastHero;
    private DateTimeOffset? lastHeroUtc;
    private bool ledgerLoaded;

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
    }

    private async void FileSystemWatcher_Created(object sender, FileSystemEventArgs e)
    {
        try
        {
            await ProcessOnceAsync(e.FullPath).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Upload of {Path} was interrupted. It stays pending until an operator retries it.",
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
        try
        {
            await SendPendingAsync().ConfigureAwait(false);
            await FileLibraryAsync().ConfigureAwait(false);
            DateTimeOffset drained = DateTimeOffset.UtcNow;
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                await Task.Delay(UploadDrain.Poll, cancellationTokenSource.Token)
                    .ConfigureAwait(false);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (!UploadDrain.ShouldDrain(settings.YouTube.DryRun, drained, now))
                {
                    continue;
                }

                drained = now;
                await SendPendingAsync().ConfigureAwait(false);
                await FileLibraryAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // shut down
        }
        finally
        {
            recordingWatcher.Created -= FileSystemWatcher_Created;
        }
    }

    public async Task ProcessRecording(string recordingPath)
    {
        CancellationToken token = cancellationTokenSource.Token;
        FileInfo recording = await WaitForRecordingReadyAsync(recordingPath, token)
            .ConfigureAwait(false);
        FileInfo entryFile = await WaitForEntryAsync(recording.Directory, token)
            .ConfigureAwait(false);
        if (entryFile == null)
        {
            logger.LogWarning(
                "No {Entry} next to {Path}; skipping upload.",
                settings.YouTube.EntryFileName,
                recordingPath
            );
            return;
        }

        YouTubeEntry entry = JsonSerializer.Deserialize<YouTubeEntry>(
            await File.ReadAllTextAsync(entryFile.FullName, token)
        );
        if (entry == null)
        {
            logger.LogWarning("YouTube entry next to {Path} was empty.", recordingPath);
            return;
        }

        YouTubeListing.StampForHost(entry, settings.YouTube, Environment.MachineName);
        if (string.IsNullOrWhiteSpace(entry.DesiredPrivacyStatus))
        {
            entry.DesiredPrivacyStatus = string.IsNullOrWhiteSpace(entry.PrivacyStatus)
                ? "public"
                : entry.PrivacyStatus;
        }

        UploadStaging.Apply(entry, settings.YouTube, Environment.MachineName);

        if (!string.IsNullOrWhiteSpace(entry.VideoId))
        {
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

            return;
        }

        DateTimeOffset dispatchedAt = DateTimeOffset.UtcNow;
        bool liveSend = settings.YouTube.Enabled != false && !settings.YouTube.DryRun;
        if (liveSend && !ReservePublicationSlot(entry, recording.FullName))
        {
            return;
        }

        var outbox = new UploadOutbox(MediaPolicyAttemptLog.AttemptsRoot(settings));
        string attemptId = await outbox
            .ContextForReplayAsync(entry.ReplayId, dispatchedAt, token)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(attemptId))
        {
            logger.LogWarning(
                "Upload of {Path} was not dispatched ({Reason}). It stays pending.",
                recording.FullName,
                UploadAttemptReasons.AttemptIdInvalid
            );
            return;
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
                operatorRetry: false
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
            return;
        }

        if (!saved.MaySend)
        {
            if (dispatched.Manifest.State == UploadAttemptState.Disabled)
            {
                logger.LogInformation(
                    "YouTube is disabled. {Path} stays pending.",
                    recording.FullName
                );
                return;
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
                await CompleteDryRunAsync(recording, entry, token).ConfigureAwait(false);
                return;
            }

            if (dispatched.Manifest.State == UploadAttemptState.AmbiguousUpload)
            {
                logger.LogInformation(
                    "Upload of {Path} stopped mid-send. It stays pending until an operator retries it. YouTube was not called.",
                    recording.FullName
                );
                return;
            }

            logger.LogInformation(
                "Upload of {Path} is already {State}. It stays pending. YouTube was not called.",
                recording.FullName,
                dispatched.Manifest.State
            );
            return;
        }

        UserCredential credential = await AuthorizeAsync().ConfigureAwait(false);

        using var youtubeService = new YouTubeService(
            new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApiKey = settings.YouTube.ApiKey,
                ApplicationName = Assembly.GetExecutingAssembly().GetName().Name,
            }
        );

        string insertPrivacy = UploadVisibility.InsertStatus(entry.PrivacyStatus);
        DateTimeOffset? publishAt = UploadVisibility.PublishAt(
            entry.DesiredPrivacyStatus,
            entry.PublishAtUtc
        );
        var videoStatus = new VideoStatus { PrivacyStatus = insertPrivacy };
        if (publishAt != null)
        {
            videoStatus.PublishAtDateTimeOffset = publishAt.Value;
        }

        var video = new Video
        {
            Snippet = new VideoSnippet
            {
                Title = entry.Title,
                Description = string.Join(Environment.NewLine, entry.DescriptionLines),
                Tags = entry.Tags,
                CategoryId = entry.CategoryId,
            },
            Status = videoStatus,
        };

        logger.LogInformation(
            "Uploading {Path} ({Bytes} bytes) as {Title} ({Privacy}).",
            recording.FullName,
            recording.Length,
            entry.Title,
            insertPrivacy
        );

        using FileStream fileStream = recording.Open(
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );
        VideosResource.InsertMediaUpload videosInsertRequest = youtubeService.Videos.Insert(
            video,
            "snippet,status",
            fileStream,
            "video/*"
        );
        Video uploaded = null;
        string capturedSession = null;
        videosInsertRequest.ProgressChanged += videosInsertRequest_ProgressChanged;
        videosInsertRequest.UploadSessionData += session =>
        {
            if (
                session?.UploadUri != null
                && UploadAttemptIds.IsSessionUri(session.UploadUri.AbsoluteUri)
            )
            {
                capturedSession = session.UploadUri.AbsoluteUri;
            }
        };
        videosInsertRequest.ResponseReceived += video =>
        {
            uploaded = video;
            videosInsertRequest_ResponseReceived(video);
        };
        bool resume = UploadAttemptIds.IsSessionUri(dispatched.Manifest?.SessionUri);
        IUploadProgress result;
        try
        {
            if (resume)
            {
                result = await videosInsertRequest
                    .ResumeAsync(new Uri(dispatched.Manifest.SessionUri), token)
                    .ConfigureAwait(false);
            }
            else
            {
                result = await videosInsertRequest.UploadAsync(token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await NoteSessionIfPresentAsync(outbox, attemptId, capturedSession)
                .ConfigureAwait(false);
            await RecordInterruptedSendAsync(outbox, attemptId).ConfigureAwait(false);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            logger.LogError(
                ex,
                "Upload of {Path} stopped mid-send. It stays pending until an operator retries it.",
                recording.FullName
            );
            return;
        }

        await NoteSessionIfPresentAsync(outbox, attemptId, capturedSession).ConfigureAwait(false);
        if (result.Status == UploadStatus.Completed && !string.IsNullOrWhiteSpace(uploaded?.Id))
        {
            entry.VideoId = uploaded.Id;
            entry.ActualPrivacyStatus = string.IsNullOrWhiteSpace(uploaded.Status?.PrivacyStatus)
                ? UploadVisibility.Staged
                : uploaded.Status.PrivacyStatus;
            await File.WriteAllTextAsync(
                    entryFile.FullName,
                    JsonSerializer.Serialize(
                        entry,
                        new JsonSerializerOptions { WriteIndented = true }
                    ),
                    token
                )
                .ConfigureAwait(false);
            logger.LogInformation(
                "Saved YouTube video id {VideoId} for replay {ReplayId} at {Privacy}.",
                entry.VideoId,
                entry.ReplayId,
                entry.ActualPrivacyStatus
            );
            UploadAttemptResult recorded = await outbox
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

            EnsureLedger();
            insertsToday++;
            lastInsertUtc = DateTimeOffset.UtcNow;
            RememberUploaded(entry);
            if (
                UploadVisibility.ReconcileUntilPublic(
                    entry.ActualPrivacyStatus,
                    entry.DesiredPrivacyStatus
                )
            )
            {
                stuckPrivate++;
                logger.LogInformation(
                    "Replay {ReplayId} uploaded as {Privacy}. It stays pending until YouTube reports public.",
                    entry.ReplayId,
                    entry.ActualPrivacyStatus ?? UploadVisibility.Staged
                );
            }
            else if (
                UploadVisibility.CountsAsPublic(
                    entry.ActualPrivacyStatus,
                    entry.DesiredPrivacyStatus
                )
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
            MediaRetention.SweepAndLog(settings, logger);
        }
        else
        {
            await RecordInterruptedSendAsync(outbox, attemptId).ConfigureAwait(false);
            logger.LogWarning(
                "Upload of {Path} stopped mid-send ({Status}). It stays pending until an operator retries it.",
                recording.FullName,
                result.Status
            );
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

    private async Task SendPendingAsync()
    {
        IReadOnlyList<string> pending = PendingYouTubeUpload.Find(
            settings.ContextsDirectory,
            settings.YouTube.EntryFileName,
            settings.YouTube.EntryFileNameUploaded
        );
        if (pending.Count == 0)
        {
            return;
        }

        logger.LogInformation(
            "Found {Count} recording(s) waiting to be sent to YouTube.",
            pending.Count
        );
        foreach (string path in pending)
        {
            try
            {
                await ProcessOnceAsync(path).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation(
                    "Upload of {Path} was interrupted. It stays pending until an operator retries it.",
                    path
                );
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not upload {Path}", path);
            }
        }

        LogPublicationHealth(pending.Count, pending);
    }

    private bool ReservePublicationSlot(YouTubeEntry entry, string path)
    {
        EnsureLedger();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset quotaDay = PublicationSchedule.QuotaDayStart(now);
        if (currentQuotaDay != quotaDay)
        {
            currentQuotaDay = quotaDay;
            insertsToday = 0;
        }

        bool production = TwitchIngestGuard.IsProductionHost(Environment.MachineName);
        string ledgerPath =
            settings.Location?.DataDirectory == null
                ? null
                : Path.Combine(settings.Location.DataDirectory, "publication-reservations.txt");
        PublicationReservationResult reserved = PublicationReservation.TryReserve(
            ledgerPath,
            production,
            insertsToday,
            now,
            lastPublicUtc,
            publicAtUtc,
            RequestedInDay(now),
            entry?.Requested == true,
            entry?.RecordedAtUtc,
            entry?.Map,
            lastMap,
            lastMapUtc,
            entry?.Hero,
            lastHero,
            lastHeroUtc,
            WorkKey(entry, path)
        );
        if (reserved.Kind == PublicationReservation.Terminal)
        {
            logger.LogInformation(
                "Upload of {Path} is past the publication window ({Reason}). It is not reserved again.",
                path,
                reserved.Reason
            );
            return false;
        }

        if (reserved.Allow)
        {
            return true;
        }

        deferredBySchedule++;
        logger.LogInformation(
            "Upload of {Path} waits for the publication schedule ({Reason}). It stays pending.",
            path,
            reserved.Reason
        );
        LogPublicationHealth(1, new[] { path });
        return false;
    }

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

    private void LogPublicationHealth(int pending, IReadOnlyList<string> candidates)
    {
        EnsureLedger();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PublicationHealthReport health = PublicationHealth.Summarize(
            pending,
            insertsToday,
            deferredBySchedule,
            insertsToday >= PublicationSchedule.MaxInsertsPerQuotaDay,
            "1",
            PublicationSchedule.PublishedIn(publicAtUtc, now, TimeSpan.FromHours(24)),
            PublicationSchedule.PublishedIn(publicAtUtc, now, TimeSpan.FromDays(7)),
            stuckPrivate
        );
        logger.LogInformation(
            "YouTube publication health pending {Pending} uploaded {Uploaded} deferred {Deferred} published-day {PublishedDay} published-week {PublishedWeek} stuck-private {StuckPrivate} limit {Limit} policy {Policy}.",
            health.Pending,
            health.Uploaded,
            health.Deferred,
            health.PublishedDay,
            health.PublishedWeek,
            health.StuckPrivate,
            health.Limit,
            health.PolicyVersion
        );
        if (string.IsNullOrWhiteSpace(settings.Location?.DataDirectory))
        {
            return;
        }

        int reserved = PublicationSchedule.ReservedRequestSlotsPerDay - RequestedInDay(now);
        if (reserved < 0)
        {
            reserved = 0;
        }

        DateTimeOffset next =
            lastPublicUtc == null
                ? now
                : lastPublicUtc.Value.Add(PublicationSchedule.MinimumInterval);
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

    private async Task ProcessOnceAsync(string recordingPath)
    {
        if (string.IsNullOrWhiteSpace(recordingPath))
        {
            return;
        }

        string key = Path.GetFullPath(recordingPath);
        if (!uploadsInFlight.TryAdd(key, 0))
        {
            logger.LogInformation("Upload of {Path} is already in progress.", key);
            return;
        }

        try
        {
            await ProcessRecording(key).ConfigureAwait(false);
        }
        finally
        {
            uploadsInFlight.TryRemove(key, out _);
        }
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
        logger.LogInformation(
            "YouTube OAuth ready for channel {ChannelId}.",
            settings.YouTube.ChannelId
        );
        return credential;
    }

    private async Task<FileInfo> WaitForRecordingReadyAsync(
        string recordingPath,
        CancellationToken token
    )
    {
        long lastLength = -1;
        int stable = 0;
        while (!token.IsCancellationRequested)
        {
            var recording = new FileInfo(recordingPath);
            recording.Refresh();
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
    }

    private async Task CompleteDryRunAsync(
        FileInfo recording,
        YouTubeEntry entry,
        CancellationToken token
    )
    {
        string receiptPath = Path.Combine(recording.Directory.FullName, "youtube-dry-run.json");
        string receipt = JsonSerializer.Serialize(
            new
            {
                entry.Title,
                entry.PrivacyStatus,
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

    private async Task FileLibraryAsync()
    {
        if (library == null)
        {
            return;
        }

        try
        {
            await library.RunOnceAsync(cancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "YouTube library pass did not finish. Uploads continue.");
        }
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

        stuckPrivate = saved.StuckPrivate < 0 ? 0 : saved.StuckPrivate;
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
                StuckPrivate = stuckPrivate,
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

    private void videosInsertRequest_ResponseReceived(Video video)
    {
        logger.LogInformation("YouTube upload complete id={VideoId}", video?.Id);
    }

    private void videosInsertRequest_ProgressChanged(IUploadProgress progress)
    {
        if (progress.Exception != null)
        {
            logger.LogError(progress.Exception, "Could not upload video.");
        }

        logger.LogInformation($"video status: {progress.Status}. bytes: ({progress.BytesSent})");
    }
}
