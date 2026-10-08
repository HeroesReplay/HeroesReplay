using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions;

namespace HeroesReplay.HeroesProfile.Client;

public partial class HeroesProfileClient
{
    /// <summary>
    /// Copies the original <c>.StormReplay</c> file for <paramref name="replayId"/> into
    /// <paramref name="destination"/>. It is the generated <c>/download/replay</c> request, but an
    /// error answer throws <see cref="HeroesProfileApiException"/> with its status and the body's
    /// <c>error.code</c> (#361): a 403 is <c>replay_deleted</c> when Heroes Profile deleted the file,
    /// and a key or plan problem otherwise. The generated error mapping has no 403, so Kiota would
    /// drop that body.
    /// </summary>
    public async Task DownloadReplayAsync(
        int replayId,
        Stream destination,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(destination);
        var handler = new NativeResponseHandler();
        await Download
            .Replay.GetAsync(
                config =>
                {
                    config.QueryParameters.ReplayID = replayId;
                    config.Options.Add(new ResponseHandlerOption { ResponseHandler = handler });
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        using HttpResponseMessage response =
            handler.Value as HttpResponseMessage
            ?? throw new InvalidOperationException(
                $"Heroes Profile returned no response for replay {replayId}."
            );
        if (!response.IsSuccessStatusCode)
        {
            string code = await HeroesProfileErrorBody
                .ReadCodeAsync(response.Content, cancellationToken)
                .ConfigureAwait(false);
            throw new HeroesProfileApiException((int)response.StatusCode, code);
        }

        if (
            response.StatusCode == HttpStatusCode.NoContent
            || response.Content.Headers.ContentLength == 0
        )
        {
            throw new InvalidOperationException(
                $"Heroes Profile v1 download returned no content for replay {replayId}."
            );
        }

        await using Stream network = await response
            .Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await network.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Heroes Profile answered with an error status. <see cref="ErrorCode"/> is the body's
/// <c>error.code</c> (<c>replay_deleted</c>, <c>endpoint_not_in_plan</c>, ...) when it had one. The
/// body itself is not kept, so nothing else from it reaches a log.
/// </summary>
public sealed class HeroesProfileApiException : ApiException
{
    public HeroesProfileApiException(int statusCode, string errorCode)
        : base(
            string.IsNullOrEmpty(errorCode)
                ? $"Heroes Profile answered HTTP {statusCode}."
                : $"Heroes Profile answered HTTP {statusCode} ({errorCode})."
        )
    {
        ResponseStatusCode = statusCode;
        ErrorCode = string.IsNullOrEmpty(errorCode) ? null : errorCode;
    }

    /// <summary>The body's <c>error.code</c>, or null when it had none.</summary>
    public string ErrorCode { get; }
}

/// <summary>
/// The <c>error.code</c> of a Heroes Profile error body:
/// <c>{"error":{"code":"replay_deleted","message":"...","endpoint":"replay_download"}}</c>.
/// </summary>
public static class HeroesProfileErrorBody
{
    /// <summary>The most of an error body that is read. An error body is a few hundred bytes.</summary>
    public const int MaxBytes = 16 * 1024;

    private const int MaxCodeLength = 64;

    /// <summary>
    /// The code from <paramref name="content"/>, read up to <see cref="MaxBytes"/>. Null when the
    /// body has none, is not JSON, or cannot be read: the status still decides.
    /// </summary>
    public static async Task<string> ReadCodeAsync(
        HttpContent content,
        CancellationToken cancellationToken
    )
    {
        if (content == null)
        {
            return null;
        }

        try
        {
            await using Stream stream = await content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            byte[] buffer = new byte[MaxBytes];
            int total = 0;
            int read;
            while (
                total < buffer.Length
                && (
                    read = await stream
                        .ReadAsync(buffer.AsMemory(total), cancellationToken)
                        .ConfigureAwait(false)
                ) > 0
            )
            {
                total += read;
            }

            return Code(Encoding.UTF8.GetString(buffer, 0, total));
        }
        catch (Exception e) when (e is IOException or HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// The code, only when it is a short token of letters, digits, <c>_</c>, <c>-</c>, or
    /// <c>.</c>. Anything else is null, so the body cannot put other text into a log.
    /// </summary>
    public static string Code(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith('{'))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out JsonElement error)
                || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("code", out JsonElement code)
                || code.ValueKind != JsonValueKind.String
            )
            {
                return null;
            }

            string value = code.GetString()?.Trim();
            return IsToken(value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsToken(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxCodeLength)
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
