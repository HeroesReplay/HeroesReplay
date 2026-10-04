using System;
using System.Collections.Generic;
using System.Diagnostics;
using HeroesReplay.Core.Telemetry;
using Xunit;

namespace HeroesReplay.Tests.Unit.Telemetry;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesReplayTelemetryTests
{
    [Fact]
    public void StartSpan_EmitsNamedActivity()
    {
        var names = new List<string>();
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == HeroesReplayTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => names.Add(activity.OperationName),
        };
        ActivitySource.AddActivityListener(listener);

        using Activity parent = HeroesReplayTelemetry.StartSpan("heroesreplay.session");
        using Activity child = HeroesReplayTelemetry.StartSpan("heroesreplay.focus.swap", parent);
        child?.SetTag("focus.hero", "Li-Ming");

        Assert.Contains("heroesreplay.session", names);
        Assert.Contains("heroesreplay.focus.swap", names);
        Assert.Equal(parent.Context.TraceId, child.Context.TraceId);
        Assert.Equal(parent.Context.SpanId, child.ParentSpanId);
    }

    [Fact]
    public void BeginReplaySession_StartsItsOwnTraceWhenAnotherActivityIsCurrent()
    {
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == HeroesReplayTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        Activity previous = Activity.Current;
        using var ambient = new Activity("process");
        ambient.SetIdFormat(ActivityIdFormat.W3C);
        ambient.Start();
        try
        {
            using Activity session = HeroesReplayTelemetry.BeginReplaySession(424242);
            Assert.NotNull(session);
            Assert.NotEqual(ambient.TraceId, session.TraceId);
            Assert.NotEqual(ambient.SpanId, session.ParentSpanId);
            Assert.Equal(424242, Convert.ToInt32(session.GetTagItem("replay.id")));

            string stored = HeroesReplayTelemetry.FormatSession(session, 424242);
            session.Dispose();
            Assert.Same(ambient, Activity.Current);

            using Activity joined = HeroesReplayTelemetry.JoinReplaySession(
                stored,
                "heroesreplay.session.joined"
            );
            Assert.NotNull(joined);
            Assert.Equal(session.TraceId, joined.TraceId);
            Assert.NotEqual(ambient.TraceId, joined.TraceId);
            Assert.Equal(424242, Convert.ToInt32(joined.GetTagItem("replay.id")));
        }
        finally
        {
            ambient.Stop();
            Activity.Current = previous;
        }
    }

    [Fact]
    public void JoinReplaySession_ChildKeepsParentTraceIdAndReplayId()
    {
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == HeroesReplayTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        const int replayId = 424242;
        Activity previous = Activity.Current;
        try
        {
            using Activity parent = HeroesReplayTelemetry.BeginReplaySession(replayId);
            string session = HeroesReplayTelemetry.FormatSession(parent, replayId);
            ActivityTraceId traceId = parent.TraceId;
            ActivitySpanId spanId = parent.SpanId;
            Assert.True(HeroesReplayTelemetry.TryParseSession(session, out int parsedId, out _));
            Assert.Equal(replayId, parsedId);
            parent.Dispose();
            Activity.Current = null;

            using Activity child = HeroesReplayTelemetry.JoinReplaySession(
                session,
                "heroesreplay.session.joined"
            );
            Assert.NotNull(child);
            Assert.Equal(traceId, child.TraceId);
            Assert.Equal(spanId, child.ParentSpanId);
            Assert.Equal(replayId, Convert.ToInt32(parent.GetTagItem("replay.id")));
            Assert.Equal(replayId, Convert.ToInt32(child.GetTagItem("replay.id")));
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Fact]
    public void JoinReplaySession_RejectsUnparsedSession()
    {
        Assert.False(
            HeroesReplayTelemetry.TryParseSession("not-a-session", out int replayId, out _)
        );
        Assert.Equal(0, replayId);
        Assert.Null(
            HeroesReplayTelemetry.JoinReplaySession("not-a-session", "heroesreplay.session.joined")
        );
        Assert.Null(HeroesReplayTelemetry.FormatSession(null, 424242));
    }
}
