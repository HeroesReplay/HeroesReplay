using System.Collections.Generic;
using System.Diagnostics;
using HeroesReplay.Core;
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
}
