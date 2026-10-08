using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.HeroesProfile.Client;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>The global statistics calls the hero statistics refresh makes.</summary>
public interface IHeroStatsApi
{
    Task<HeroesProfileGlobalAnswer> GetAsync(
        string path,
        IReadOnlyList<KeyValuePair<string, string>> query,
        CancellationToken cancellationToken
    );

    Task<HeroesProfileGlobalAnswer> GetJobAsync(string jobId, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IHeroStatsApi"/> over its own <see cref="HeroesProfileClient"/>. Its
/// <c>HttpClient</c> has no retry handler: the refresh reads 202, 429, and <c>Retry-After</c>
/// itself instead of retrying a rate limit every second.
/// </summary>
public sealed class HeroStatsApi : IHeroStatsApi
{
    public const string HttpClientName = "heroes-profile-stats";

    private readonly HeroesProfileClient client;

    public HeroStatsApi(HeroesProfileClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public Task<HeroesProfileGlobalAnswer> GetAsync(
        string path,
        IReadOnlyList<KeyValuePair<string, string>> query,
        CancellationToken cancellationToken
    ) => client.GetGlobalAsync(path, query, cancellationToken);

    public Task<HeroesProfileGlobalAnswer> GetJobAsync(
        string jobId,
        CancellationToken cancellationToken
    ) => client.GetJobAsync(jobId, cancellationToken);
}

public enum HeroStatsFailure
{
    /// <summary>401 or 403: the key cannot make this call. The refresh stops until a restart.</summary>
    AccessDenied,

    /// <summary>422: a parameter was refused, such as a patch without data yet. This pass stops.</summary>
    Rejected,

    /// <summary>Anything else: a job that failed or timed out, a 404, or a server or network error.</summary>
    Unavailable,
}

public sealed class HeroStatsException : Exception
{
    public HeroStatsException(
        HeroStatsFailure failure,
        string message,
        int? statusCode = null,
        string errorCode = null,
        Exception inner = null
    )
        : base(message, inner)
    {
        Failure = failure;
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public HeroStatsFailure Failure { get; }
    public int? StatusCode { get; }
    public string ErrorCode { get; }
}
