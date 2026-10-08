using System;
using System.Collections.Generic;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.ServiceHost;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

/// <summary>#358: a refused Heroes Profile key pauses the replay list at the probe's cadence.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayListBackoffTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 16, 1, 5, TimeSpan.Zero);
    private static readonly TimeSpan Wait = TimeSpan.FromMinutes(2);

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void ARefusedList_AllowsOneTryPerWait_AndLogsOnce(int status)
    {
        var log = new ListLogger();
        var backoff = new ReplayListBackoff(Wait, log);

        Assert.True(backoff.TryList(Now));
        backoff.Record(ReplayListing.Rejected(status), Now);

        Assert.True(backoff.Paused);
        Assert.False(backoff.TryList(Now));
        Assert.False(backoff.TryList(Now + Wait - TimeSpan.FromSeconds(1)));
        Assert.True(backoff.TryList(Now + Wait));
        // That try is the turn: nothing more until the next wait.
        Assert.False(backoff.TryList(Now + Wait));
        backoff.Record(ReplayListing.Rejected(status), Now + Wait);
        Assert.False(backoff.TryList(Now + Wait + Wait - TimeSpan.FromSeconds(1)));
        Assert.True(backoff.TryList(Now + Wait + Wait));

        string warning = Assert.Single(log.Lines);
        Assert.StartsWith("Warning:", warning, StringComparison.Ordinal);
        Assert.Contains($"(HTTP {status})", warning);
        Assert.Contains("one try every 2m", warning);
    }

    /// <summary>A 429, a 5xx, or no answer stays transient: nothing pauses, nothing logs.</summary>
    [Fact]
    public void AnUnansweredList_DoesNotPause()
    {
        var log = new ListLogger();
        var backoff = new ReplayListBackoff(Wait, log);

        backoff.Record(ReplayListing.Unanswered, Now);

        Assert.False(backoff.Paused);
        Assert.True(backoff.TryList(Now));
        Assert.True(backoff.TryList(Now));
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void AnAnsweredList_ResumesAndLogsOnce()
    {
        var log = new ListLogger();
        var backoff = new ReplayListBackoff(Wait, log);
        backoff.Record(ReplayListing.Rejected(401), Now);

        Assert.True(backoff.TryList(Now + Wait));
        backoff.Record(ReplayListing.Empty, Now + Wait);
        backoff.Record(ReplayListing.Empty, Now + Wait);

        Assert.False(backoff.Paused);
        Assert.True(backoff.TryList(Now + Wait));
        Assert.True(backoff.TryList(Now + Wait));
        Assert.Equal(2, log.Lines.Count);
        Assert.Equal(
            "Information: Heroes Profile answered the replay list. Replay listing resumes.",
            log.Lines[1]
        );
    }

    /// <summary>An unanswered try while paused keeps the pause: only an answer ends it.</summary>
    [Fact]
    public void AnUnansweredTryWhilePaused_KeepsThePause()
    {
        var backoff = new ReplayListBackoff(Wait);
        backoff.Record(ReplayListing.Rejected(401), Now);

        Assert.True(backoff.TryList(Now + Wait));
        backoff.Record(ReplayListing.Unanswered, Now + Wait);

        Assert.True(backoff.Paused);
        Assert.False(backoff.TryList(Now + Wait + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ARejectedProbe_PausesBeforeAnyList_UntilAProbePasses()
    {
        var log = new ListLogger();
        var backoff = new ReplayListBackoff(Wait, log);

        backoff.Observe(Rejected(), Now);

        Assert.True(backoff.Paused);
        // No turn at all while the probe says rejected, however long it lasts.
        Assert.False(backoff.TryList(Now + TimeSpan.FromHours(1)));
        backoff.Observe(Rejected(), Now + Wait);
        Assert.False(backoff.TryList(Now + TimeSpan.FromHours(2)));

        backoff.Observe(Ok(), Now + TimeSpan.FromHours(2));

        Assert.False(backoff.Paused);
        Assert.True(backoff.TryList(Now + TimeSpan.FromHours(2)));
        Assert.Equal(2, log.Lines.Count);
        Assert.Contains("download.heroesprofile_rejected", log.Lines[0]);
        Assert.Equal(
            "Information: The Heroes Profile probe passed. Replay listing resumes.",
            log.Lines[1]
        );
    }

    [Fact]
    public void APassingProbe_ResumesARefusedListAtOnce()
    {
        var backoff = new ReplayListBackoff(Wait);
        backoff.Record(ReplayListing.Rejected(401), Now);

        backoff.Observe(Ok(), Now + TimeSpan.FromSeconds(5));

        Assert.False(backoff.Paused);
        Assert.True(backoff.TryList(Now + TimeSpan.FromSeconds(5)));
    }

    /// <summary>An unreachable probe says nothing about the key: the wait goes on as it was.</summary>
    [Fact]
    public void AnUnreachableProbe_LeavesTheWait()
    {
        var backoff = new ReplayListBackoff(Wait);
        backoff.Observe(Rejected(), Now);

        backoff.Observe(
            ServiceDependencyResult.Unreachable(
                HeroesProfileApiProbe.DependencyName,
                HeroesProfileApiProbe.UnreachableCode,
                "timeout",
                "wait"
            ),
            Now + Wait
        );

        Assert.True(backoff.Paused);
        Assert.True(backoff.TryList(Now + Wait));
    }

    [Fact]
    public void AnotherDependency_IsIgnored()
    {
        var backoff = new ReplayListBackoff(Wait);

        backoff.Observe(
            ServiceDependencyResult.Rejected("YouTube OAuth", "youtube.oauth_invalid", "x", "y"),
            Now
        );

        Assert.False(backoff.Paused);
    }

    [Fact]
    public void ANonPositiveWait_IsTheDefaultRetryInterval()
    {
        Assert.Equal(
            ServiceHealthSettings.DefaultDependencyRetryInterval,
            new ReplayListBackoff(TimeSpan.Zero).Wait
        );
    }

    private static ServiceDependencyResult Rejected() =>
        ServiceDependencyResult.Rejected(
            HeroesProfileApiProbe.DependencyName,
            HeroesProfileApiProbe.RejectedCode,
            "Heroes Profile rejected HeroesProfileApi:ApiKey (HTTP 401).",
            "fix the key"
        );

    private static ServiceDependencyResult Ok() =>
        ServiceDependencyResult.Ok(HeroesProfileApiProbe.DependencyName, "accepted");

    private sealed class ListLogger : ILogger
    {
        public List<string> Lines { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (logLevel >= LogLevel.Information)
            {
                Lines.Add($"{logLevel}: {formatter(state, exception)}");
            }
        }
    }
}
