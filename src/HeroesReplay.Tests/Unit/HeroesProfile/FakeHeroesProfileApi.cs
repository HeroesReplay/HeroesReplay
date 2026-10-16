using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.HeroesProfile.Client;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

/// <summary>
/// Heroes Profile answers by path, in order (the last one repeats), and job answers by id. Time
/// is a fake clock that a delay moves forward, so pacing and polls are measured, not waited.
/// </summary>
internal sealed class FakeHeroesProfileApi : IHeroStatsApi
{
    private readonly Dictionary<string, Queue<HeroesProfileGlobalAnswer>> answers = new();
    private readonly Dictionary<string, Queue<HeroesProfileGlobalAnswer>> jobs = new();

    public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    public List<(string Path, Dictionary<string, string> Query, DateTimeOffset At)> Calls { get; } =
        new();

    public List<TimeSpan> Delays { get; } = new();

    public int JobPolls { get; private set; }

    public Exception Throw { get; set; }

    public void Answer(string path, params HeroesProfileGlobalAnswer[] replies) =>
        answers[path] = new Queue<HeroesProfileGlobalAnswer>(replies);

    public void Job(string jobId, params HeroesProfileGlobalAnswer[] replies) =>
        jobs[jobId] = new Queue<HeroesProfileGlobalAnswer>(replies);

    public DateTimeOffset Clock() => Now;

    public Task Delay(TimeSpan wait, CancellationToken cancellationToken)
    {
        Delays.Add(wait);
        Now += wait;
        return Task.CompletedTask;
    }

    public HeroesProfilePacer Pacer(double spacingSeconds = 1.1) =>
        new(TimeSpan.FromSeconds(spacingSeconds), Clock, Delay);

    public Task<HeroesProfileGlobalAnswer> GetAsync(
        string path,
        IReadOnlyList<KeyValuePair<string, string>> query,
        CancellationToken cancellationToken
    )
    {
        Calls.Add(
            (
                path,
                (query ?? Array.Empty<KeyValuePair<string, string>>())
                    .Where(pair => pair.Value != null)
                    .ToDictionary(pair => pair.Key, pair => pair.Value),
                Now
            )
        );
        if (Throw != null)
        {
            return Task.FromException<HeroesProfileGlobalAnswer>(Throw);
        }

        return Task.FromResult(Next(answers, path));
    }

    public Task<HeroesProfileGlobalAnswer> GetJobAsync(
        string jobId,
        CancellationToken cancellationToken
    )
    {
        JobPolls++;
        return Task.FromResult(Next(jobs, jobId));
    }

    public static HeroesProfileGlobalAnswer Ok(string body) =>
        HeroesProfileGlobalAnswer.From(200, body, null);

    public static HeroesProfileGlobalAnswer Error(
        int status,
        string code = null,
        TimeSpan? retryAfter = null
    ) =>
        HeroesProfileGlobalAnswer.From(
            status,
            code == null ? "{}" : "{\"error\":{\"code\":\"" + code + "\"}}",
            retryAfter
        );

    public static HeroesProfileGlobalAnswer Accepted(string jobId, TimeSpan? retryAfter = null) =>
        HeroesProfileGlobalAnswer.From(
            202,
            "{\"async\":true,\"status\":\"pending\",\"job_id\":\"" + jobId + "\"}",
            retryAfter
        );

    private static HeroesProfileGlobalAnswer Next(
        Dictionary<string, Queue<HeroesProfileGlobalAnswer>> source,
        string key
    )
    {
        if (
            !source.TryGetValue(key, out Queue<HeroesProfileGlobalAnswer> queue)
            || queue.Count == 0
        )
        {
            return Error(404, "not_found");
        }

        return queue.Count == 1 ? queue.Peek() : queue.Dequeue();
    }
}
