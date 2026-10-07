using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.YouTube.Outbox;

/// <summary>What the uploader does with an attempt whose send was interrupted.</summary>
public enum InterruptedUploadAction
{
    /// <summary>The automatic retries are used up. An operator decides.</summary>
    Exhausted,

    /// <summary>No session was saved, and the replay id is already on the channel. Not sent again.</summary>
    AlreadyOnYouTube,

    /// <summary>
    /// No session was saved, so YouTube never got the media (the session is saved before the
    /// first byte). A new insert cannot duplicate a video.
    /// </summary>
    Restart,

    /// <summary>A session was saved. Ask YouTube how much of the file it holds.</summary>
    Probe,

    /// <summary>YouTube finished the upload before the response was lost. Record that video.</summary>
    Confirm,

    /// <summary>The session is incomplete and its publish time is still ahead. Resume it.</summary>
    Resume,

    /// <summary>
    /// The session is incomplete and its publish time has passed. The replay gets the next valid
    /// publish time: when that is now the session is resumed, otherwise it is dropped (no video
    /// exists for it) and a new insert carries the new time.
    /// </summary>
    Reschedule,

    /// <summary>YouTube no longer knows the session, so it may have completed. An operator decides.</summary>
    SessionGone,

    /// <summary>The status query failed. The next pass asks again.</summary>
    CheckFailed,
}

/// <summary>
/// The decision for an interrupted <c>videos.insert</c>. A resumable upload creates its video
/// only when YouTube has every byte, so an incomplete session has no video and is safe to resume
/// or drop. A send cut off after its last chunk may have completed; the session's status says
/// which, so the uploader never inserts a second copy.
/// </summary>
public static class InterruptedUpload
{
    /// <summary>A new publish time this close to now is "now": the session is resumed as it is.</summary>
    public static readonly TimeSpan ReadyTolerance = TimeSpan.FromMinutes(2);

    public static InterruptedUploadAction Plan(
        int retriesUsed,
        int maxRetries,
        bool hasSession,
        bool replayOnChannel
    )
    {
        if (maxRetries <= 0 || retriesUsed >= maxRetries)
        {
            return InterruptedUploadAction.Exhausted;
        }

        if (hasSession)
        {
            return InterruptedUploadAction.Probe;
        }

        return replayOnChannel
            ? InterruptedUploadAction.AlreadyOnYouTube
            : InterruptedUploadAction.Restart;
    }

    public static InterruptedUploadAction AfterProbe(
        UploadSessionState state,
        DateTimeOffset? publishAtUtc,
        DateTimeOffset now
    )
    {
        switch (state)
        {
            case UploadSessionState.Complete:
                return InterruptedUploadAction.Confirm;
            case UploadSessionState.Gone:
                return InterruptedUploadAction.SessionGone;
            case UploadSessionState.Incomplete:
                // A private listing has no publish time, so nothing can be late.
                return publishAtUtc is DateTimeOffset at && at <= now
                    ? InterruptedUploadAction.Reschedule
                    : InterruptedUploadAction.Resume;
            default:
                return InterruptedUploadAction.CheckFailed;
        }
    }

    /// <summary>
    /// After a reschedule: true when the new publish time is now (or there is none), so the old
    /// session, whose publish time has passed, publishes the video at the right time anyway.
    /// </summary>
    public static bool ResumeAtNewTime(DateTimeOffset? newPublishAtUtc, DateTimeOffset now) =>
        newPublishAtUtc == null || newPublishAtUtc.Value <= now + ReadyTolerance;
}

/// <summary>
/// How many automatic retries each interrupted attempt has used, in
/// <c>Data\youtube-upload-retries.json</c>, so the bound holds across restarts. Removing an
/// attempt's line gives it the full number of retries again.
/// </summary>
public sealed class InterruptedUploadRetries
{
    public const string FileName = "youtube-upload-retries.json";

    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string path;

    public InterruptedUploadRetries(string dataDirectory)
    {
        path = string.IsNullOrWhiteSpace(dataDirectory)
            ? null
            : Path.Combine(dataDirectory, FileName);
    }

    public int Count(string attemptId)
    {
        if (string.IsNullOrWhiteSpace(attemptId))
        {
            return 0;
        }

        lock (Gate)
        {
            return Load().TryGetValue(attemptId, out Entry entry) ? entry.Count : 0;
        }
    }

    /// <summary>Counts one more retry for <paramref name="attemptId"/> and returns the new count.</summary>
    public int Add(string attemptId, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(attemptId))
        {
            return 0;
        }

        lock (Gate)
        {
            Dictionary<string, Entry> entries = Load();
            entries.TryGetValue(attemptId, out Entry entry);
            entry ??= new Entry();
            entry.Count++;
            entry.LastAt = at;
            entries[attemptId] = entry;
            Save(entries);
            return entry.Count;
        }
    }

    /// <summary>The attempt finished. Its line goes.</summary>
    public void Clear(string attemptId)
    {
        if (string.IsNullOrWhiteSpace(attemptId))
        {
            return;
        }

        lock (Gate)
        {
            Dictionary<string, Entry> entries = Load();
            if (entries.Remove(attemptId))
            {
                Save(entries);
            }
        }
    }

    private Dictionary<string, Entry> Load()
    {
        var empty = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (path == null)
        {
            return empty;
        }

        string json = DurableFile.ReadOrAside(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return empty;
        }

        try
        {
            Dictionary<string, Entry> read = JsonSerializer.Deserialize<Dictionary<string, Entry>>(
                json
            );
            return read == null
                ? empty
                : new Dictionary<string, Entry>(read, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            DurableFile.Aside(path);
            return empty;
        }
    }

    private void Save(Dictionary<string, Entry> entries)
    {
        if (path != null)
        {
            DurableFile.Replace(path, JsonSerializer.Serialize(entries, Options));
        }
    }

    public sealed class Entry
    {
        public int Count { get; set; }
        public DateTimeOffset? LastAt { get; set; }
    }
}
