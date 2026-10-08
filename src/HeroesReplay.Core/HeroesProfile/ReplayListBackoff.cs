using System;
using HeroesReplay.Core.ServiceHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// Keeps the download role from listing Heroes Profile while it refuses the API key (#358). A
/// 401 or 403 from the replay list is not transient: the same key gets the same answer until
/// someone fixes it. So the listing pauses. While paused it tries once every <see cref="Wait"/>
/// (the failing dependency probe's interval, 2 min), and not at all while the role's dependency
/// probe (#305) reports the key rejected. A probe that passes, or a list Heroes Profile answers,
/// resumes it at once. It logs one line per change, never one per call. A 429, a 5xx, a timeout,
/// or no answer changes nothing here: those stay transient.
/// </summary>
public sealed class ReplayListBackoff
{
    private readonly object sync = new();
    private readonly ILogger logger;
    private bool paused;
    private bool probeRejects;
    private DateTimeOffset nextTry;

    /// <param name="wait">
    /// The time between list calls while the key is refused:
    /// <see cref="ServiceHealthSettings.NextDependencyProbe"/> after a failure. Zero or less is the
    /// default retry interval, 2 minutes.
    /// </param>
    /// <param name="logger">One line when the listing pauses or resumes. Never the key.</param>
    public ReplayListBackoff(TimeSpan wait, ILogger logger = null)
    {
        Wait = wait > TimeSpan.Zero ? wait : ServiceHealthSettings.DefaultDependencyRetryInterval;
        this.logger = logger ?? NullLogger.Instance;
    }

    /// <summary>The time between list calls while the key is refused.</summary>
    public TimeSpan Wait { get; }

    /// <summary>True while the key is refused. The caller does not retry the list in that pass.</summary>
    public bool Paused
    {
        get
        {
            lock (sync)
            {
                return paused;
            }
        }
    }

    /// <summary>
    /// True when a list call may go out now. While paused that is once per <see cref="Wait"/>,
    /// and never while the probe reports the key rejected; a true answer uses that turn.
    /// </summary>
    public bool TryList(DateTimeOffset now)
    {
        lock (sync)
        {
            if (!paused)
            {
                return true;
            }

            if (probeRejects || now < nextTry)
            {
                return false;
            }

            nextTry = now + Wait;
            return true;
        }
    }

    /// <summary>What one list call returned.</summary>
    public void Record(ReplayListing page, DateTimeOffset now)
    {
        if (page?.RejectedStatus is int status)
        {
            bool started;
            lock (sync)
            {
                started = !paused;
                paused = true;
                nextTry = now + Wait;
            }

            if (started)
            {
                logger.LogWarning(
                    "Heroes Profile refused the API key on the replay list (HTTP {Status}). Listing pauses: one try every {Wait}, none while the dependency probe reports the key rejected. It resumes when a probe or a list passes. Check HeroesProfileApi:ApiKey with `heroesreplay check heroesprofile`.",
                    status,
                    ServiceHealthClassifier.Describe(Wait)
                );
            }

            return;
        }

        if (page?.Listed == true)
        {
            Resume("Heroes Profile answered the replay list");
        }
    }

    /// <summary>
    /// The role's dependency probe result. Only the Heroes Profile probe counts: rejected pauses
    /// the listing until a probe passes, ok resumes it, and unreachable leaves the wait as it is.
    /// </summary>
    public void Observe(ServiceDependencyResult result, DateTimeOffset now)
    {
        if (
            result == null
            || !string.Equals(
                result.Dependency,
                HeroesProfileApiProbe.DependencyName,
                StringComparison.Ordinal
            )
        )
        {
            return;
        }

        if (string.Equals(result.State, ServiceDependencyStates.Ok, StringComparison.Ordinal))
        {
            lock (sync)
            {
                probeRejects = false;
            }

            Resume("The Heroes Profile probe passed");
            return;
        }

        bool rejected = string.Equals(
            result.State,
            ServiceDependencyStates.Rejected,
            StringComparison.Ordinal
        );
        bool started;
        lock (sync)
        {
            probeRejects = rejected;
            started = rejected && !paused;
            if (started)
            {
                paused = true;
                nextTry = now + Wait;
            }
        }

        if (started)
        {
            logger.LogWarning(
                "The Heroes Profile probe reports the API key rejected [{Code}]. The replay list is not called until a probe passes.",
                result.Code
            );
        }
    }

    private void Resume(string why)
    {
        bool ended;
        lock (sync)
        {
            ended = paused;
            paused = false;
        }

        if (ended)
        {
            logger.LogInformation("{Why}. Replay listing resumes.", why);
        }
    }
}
