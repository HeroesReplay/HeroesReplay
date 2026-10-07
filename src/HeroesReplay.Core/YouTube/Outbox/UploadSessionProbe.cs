using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;

namespace HeroesReplay.Core.YouTube.Outbox;

public enum UploadSessionState
{
    /// <summary>The query failed (network, a server error). Nothing is known.</summary>
    Unknown,

    /// <summary>YouTube holds part of the file (308). No video exists yet.</summary>
    Incomplete,

    /// <summary>YouTube holds every byte and created the video (200 or 201).</summary>
    Complete,

    /// <summary>YouTube does not know the session (404 or 410): expired, or finished long ago.</summary>
    Gone,
}

public sealed class UploadSessionStatus
{
    public UploadSessionState State { get; init; }

    /// <summary>Bytes YouTube holds, for an incomplete session.</summary>
    public long BytesReceived { get; init; }

    /// <summary>The video YouTube created, for a complete session.</summary>
    public Video Video { get; init; }

    public int? HttpStatus { get; init; }

    public string Detail { get; init; }
}

/// <summary>
/// Asks a resumable upload session how much of the file YouTube holds, without sending any of
/// it: <c>PUT</c> with an empty body and <c>Content-Range: bytes */size</c>. This is the status
/// query of the resumable upload protocol. It costs no quota.
/// </summary>
public static class UploadSessionProbe
{
    public static async Task<UploadSessionStatus> ProbeAsync(
        HttpClient http,
        string sessionUri,
        long length,
        Func<string, Video> readVideo,
        CancellationToken cancellationToken
    )
    {
        if (http == null || !UploadAttemptIds.IsSessionUri(sessionUri) || length <= 0)
        {
            return new UploadSessionStatus
            {
                State = UploadSessionState.Unknown,
                Detail = "no session to ask",
            };
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, sessionUri)
            {
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };
            request.Content.Headers.ContentRange = new ContentRangeHeaderValue(length);
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (status == 308)
            {
                return new UploadSessionStatus
                {
                    State = UploadSessionState.Incomplete,
                    BytesReceived = ReceivedBytes(response),
                    HttpStatus = status,
                };
            }

            if (response.IsSuccessStatusCode)
            {
                string body = await response
                    .Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                Video video = string.IsNullOrWhiteSpace(body) ? null : readVideo?.Invoke(body);
                if (string.IsNullOrWhiteSpace(video?.Id))
                {
                    return new UploadSessionStatus
                    {
                        State = UploadSessionState.Unknown,
                        HttpStatus = status,
                        Detail = "a finished session without a video id",
                    };
                }

                return new UploadSessionStatus
                {
                    State = UploadSessionState.Complete,
                    BytesReceived = length,
                    Video = video,
                    HttpStatus = status,
                };
            }

            if (
                response.StatusCode == HttpStatusCode.NotFound
                || response.StatusCode == HttpStatusCode.Gone
            )
            {
                return new UploadSessionStatus
                {
                    State = UploadSessionState.Gone,
                    HttpStatus = status,
                };
            }

            return new UploadSessionStatus
            {
                State = UploadSessionState.Unknown,
                HttpStatus = status,
                Detail = "HTTP " + status,
            };
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            // A network failure, a timeout, or a body that is not a video: nothing is known.
            return new UploadSessionStatus
            {
                State = UploadSessionState.Unknown,
                Detail = exception.GetType().Name + ": " + exception.Message,
            };
        }
    }

    /// <summary>
    /// <c>Range: bytes=0-N</c> means N + 1 bytes are held. No Range header means none.
    /// </summary>
    internal static long ReceivedBytes(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Range", out var values))
        {
            return 0;
        }

        string range = values.FirstOrDefault();
        int dash = range?.LastIndexOf('-') ?? -1;
        if (
            dash < 0
            || !long.TryParse(
                range.Substring(dash + 1),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out long last
            )
        )
        {
            return 0;
        }

        return last + 1;
    }
}
