using System;
using System.Collections.Generic;
using HeroesReplay.Core.HeroesProfile;

namespace HeroesReplay.Core.Requests;

/// <summary>
/// The download role's record of one request's replay download (#351). A request stays in
/// <c>Data\requests.json</c> until its replay is on disk. A failed attempt is kept here with the
/// time of the next one, and a request that can never be downloaded keeps its reason in the
/// failed file.
/// </summary>
public sealed class RequestDownload
{
    /// <summary>Failed attempts so far. A stop is not an attempt.</summary>
    public int Attempts { get; set; }

    /// <summary>The download is not tried again before this time.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>What the last attempt failed with, for the log and the queue page.</summary>
    public string LastError { get; set; }

    /// <summary>When the request was given up for good. Only on a record in the failed file.</summary>
    public DateTimeOffset? FailedAt { get; set; }

    /// <summary>Why the request was given up, in words a viewer can read on the queue page.</summary>
    public string FailureReason { get; set; }

    /// <summary>
    /// A cancel was recorded for <c>twitch connect</c> to send, which returns the viewer's points.
    /// </summary>
    public bool RefundRequested { get; set; }
}

public enum RequestDownloadVerdict
{
    /// <summary>The request stays queued and its download is tried again later.</summary>
    Retry,

    /// <summary>The replay can never be downloaded. The request is failed and refunded.</summary>
    Fail,
}

/// <summary>
/// When a requested replay's download is given up (#351). Only an answer that says the file is
/// gone fails a request: Heroes Profile's 404 or 410, or a 403 whose body's <c>error.code</c> is
/// <c>replay_deleted</c> (#361). Anything else (a 429, a 5xx, any other 403, another status, a
/// timeout, a network or disk error) can succeed later, so the request stays queued and its
/// points are never refunded for it.
/// </summary>
public static class RequestDownloadRetry
{
    public static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The <c>error.code</c> of Heroes Profile's 403 for a replay it deleted: "That replay is no
    /// longer stored."
    /// </summary>
    public const string ReplayDeletedCode = "replay_deleted";

    /// <summary>
    /// <paramref name="httpStatus"/> is the status Heroes Profile answered the download with
    /// after the HTTP pipeline's own retries, or null when it did not answer.
    /// <paramref name="errorCode"/> is the body's <c>error.code</c>, or null. A 403 is a key or plan
    /// problem unless that code says the replay was deleted.
    /// </summary>
    public static RequestDownloadVerdict Classify(int? httpStatus, string errorCode = null) =>
        httpStatus is 404 or 410
        || (
            httpStatus == 403
            && string.Equals(errorCode, ReplayDeletedCode, StringComparison.OrdinalIgnoreCase)
        )
            ? RequestDownloadVerdict.Fail
            : RequestDownloadVerdict.Retry;

    /// <summary>
    /// The answer in a reason a person reads: <c>HTTP 403 replay_deleted</c>, or <c>HTTP 404</c>
    /// when the body had no code.
    /// </summary>
    public static string Describe(int httpStatus, string errorCode) =>
        string.IsNullOrWhiteSpace(errorCode)
            ? $"HTTP {httpStatus}"
            : $"HTTP {httpStatus} {errorCode}";

    /// <summary>The wait after the <paramref name="attempts"/>th failed attempt: 1, 2, 4, 8, 16, then 30 min.</summary>
    public static TimeSpan Delay(int attempts)
    {
        if (attempts <= 1)
        {
            return FirstDelay;
        }

        double minutes = FirstDelay.TotalMinutes * Math.Pow(2, Math.Min(attempts - 1, 16));
        return minutes >= MaxDelay.TotalMinutes ? MaxDelay : TimeSpan.FromMinutes(minutes);
    }

    /// <summary>
    /// False only when the replay's client version is known and the supported patch line no
    /// longer includes it (the same rule the request was queued under). An unknown version is
    /// left to the spectator.
    /// </summary>
    public static bool OnSupportedLine(
        string gameVersion,
        IEnumerable<string> exactVersions,
        string minimumVersion
    ) =>
        string.IsNullOrWhiteSpace(gameVersion)
        || GameVersionOrder.Allows(gameVersion, exactVersions, minimumVersion);
}
