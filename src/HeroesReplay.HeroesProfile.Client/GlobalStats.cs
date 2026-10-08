using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions;

namespace HeroesReplay.HeroesProfile.Client;

/// <summary>
/// The global statistics calls (<c>/heroes</c>, <c>/patches</c>, <c>/heroes/stats</c>,
/// <c>/heroes/matchups</c>) and the <c>/jobs/{id}</c> poll. They are not in the generated client:
/// an uncached global query answers 202 with a <c>job_id</c>, and the result is collected from
/// <c>/jobs/{id}</c>, which the generated models cannot express. Each call returns the status,
/// body, and <c>Retry-After</c> so the caller decides what to do with 202, 429, and errors.
/// </summary>
public partial class HeroesProfileClient
{
    public Task<HeroesProfileGlobalAnswer> GetGlobalAsync(
        string path,
        IEnumerable<KeyValuePair<string, string>> query,
        CancellationToken cancellationToken = default
    )
    {
        RequestInformation request = CreateGlobalRequest(RequestAdapter.BaseUrl, path, query);
        return SendGlobalAsync(request, cancellationToken);
    }

    public Task<HeroesProfileGlobalAnswer> GetJobAsync(
        string jobId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("A job id is required.", nameof(jobId));
        }

        RequestInformation request = CreateGlobalRequest(
            RequestAdapter.BaseUrl,
            "jobs/" + Uri.EscapeDataString(jobId.Trim()),
            null
        );
        return SendGlobalAsync(request, cancellationToken);
    }

    /// <summary>A GET for <paramref name="path"/> under the v1 base. Blank query values are left out.</summary>
    public static RequestInformation CreateGlobalRequest(
        string baseUrl,
        string path,
        IEnumerable<KeyValuePair<string, string>> query
    )
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentNullException(nameof(baseUrl));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentNullException(nameof(path));
        }

        var url = new StringBuilder(baseUrl.TrimEnd('/'))
            .Append('/')
            .Append(path.Trim().TrimStart('/'));
        char separator = '?';
        if (query != null)
        {
            foreach (KeyValuePair<string, string> pair in query)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                url.Append(separator)
                    .Append(Uri.EscapeDataString(pair.Key))
                    .Append('=')
                    .Append(Uri.EscapeDataString(pair.Value));
                separator = '&';
            }
        }

        var request = new RequestInformation
        {
            HttpMethod = Method.GET,
            URI = new Uri(url.ToString()),
        };
        request.Headers.TryAdd("Accept", "application/json");
        return request;
    }

    private async Task<HeroesProfileGlobalAnswer> SendGlobalAsync(
        RequestInformation request,
        CancellationToken cancellationToken
    )
    {
        var handler = new NativeResponseHandler();
        request.AddRequestOptions(
            new IRequestOption[] { new ResponseHandlerOption { ResponseHandler = handler } }
        );
        await RequestAdapter
            .SendNoContentAsync(request, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using HttpResponseMessage response =
            handler.Value as HttpResponseMessage
            ?? throw new InvalidOperationException("Heroes Profile returned no response.");
        string body =
            response.Content == null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string headerJob = null;
        if (response.Headers.TryGetValues("x-global-job-id", out IEnumerable<string> jobs))
        {
            foreach (string value in jobs)
            {
                headerJob = string.IsNullOrWhiteSpace(value) ? headerJob : value.Trim();
            }
        }

        return HeroesProfileGlobalAnswer.From(
            (int)response.StatusCode,
            body,
            RetryAfter(response),
            headerJob
        );
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry?.Delta is TimeSpan delta && delta >= TimeSpan.Zero)
        {
            return delta;
        }

        if (retry?.Date is DateTimeOffset when)
        {
            TimeSpan wait = when - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }
}

/// <summary>One answer from a global statistics call or a job poll.</summary>
public sealed class HeroesProfileGlobalAnswer
{
    public int StatusCode { get; init; }
    public string Body { get; init; }

    /// <summary>The <c>Retry-After</c> header, or null when the answer had none.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>The job to poll after a 202: the <c>x-global-job-id</c> header or the body's <c>job_id</c>.</summary>
    public string JobId { get; init; }

    /// <summary>The <c>error.code</c> of an error body, such as <c>rate_limited</c> or <c>endpoint_not_in_plan</c>.</summary>
    public string ErrorCode { get; init; }

    public static HeroesProfileGlobalAnswer From(
        int statusCode,
        string body,
        TimeSpan? retryAfter,
        string headerJobId = null
    )
    {
        string jobId = headerJobId;
        string errorCode = null;
        if (!string.IsNullOrWhiteSpace(body) && body.TrimStart().StartsWith('{'))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                JsonElement root = document.RootElement;
                if (
                    jobId == null
                    && root.TryGetProperty("job_id", out JsonElement job)
                    && job.ValueKind == JsonValueKind.String
                )
                {
                    jobId = job.GetString();
                }

                if (
                    root.TryGetProperty("error", out JsonElement error)
                    && error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("code", out JsonElement code)
                    && code.ValueKind == JsonValueKind.String
                )
                {
                    errorCode = code.GetString();
                }
            }
            catch (JsonException)
            {
                // Not JSON after all. The caller still has the status and body.
            }
        }

        return new HeroesProfileGlobalAnswer
        {
            StatusCode = statusCode,
            Body = body ?? string.Empty,
            RetryAfter = retryAfter,
            JobId = string.IsNullOrWhiteSpace(jobId) ? null : jobId.Trim(),
            ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? null : errorCode.Trim(),
        };
    }
}
