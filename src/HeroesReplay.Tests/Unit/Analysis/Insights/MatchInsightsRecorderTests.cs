using System;
using System.Collections.Generic;
using System.IO;
using Heroes.ReplayParser;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Shared;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchInsightsRecorderTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "hr-insights-" + Guid.NewGuid().ToString("N")
    );

    [Fact]
    public void Record_WritesInsightsJsonNextToTheRecording()
    {
        var insights = new MatchInsights();
        var recorder = new MatchInsightsRecorder(
            new FixedReader(insights),
            NullLogger<MatchInsightsRecorder>.Instance
        );

        MatchInsights recorded = recorder.Record(new Replay(), Array.Empty<Hero>(), null, folder);

        Assert.Same(insights, recorded);
        string json = File.ReadAllText(Path.Combine(folder, InsightsJson.FileName));
        Assert.Equal(MatchInsights.CurrentVersion, InsightsJson.Parse(json).Version);
    }

    [Fact]
    public void Record_ReturnsNullWhenTheInsightsCannotBeRead_SoTheEntryIsWrittenWithoutThem()
    {
        var recorder = new MatchInsightsRecorder(
            new FixedReader(null),
            NullLogger<MatchInsightsRecorder>.Instance
        );

        Assert.Null(recorder.Record(new Replay(), Array.Empty<Hero>(), null, folder));
        Assert.Null(recorder.Record(null, Array.Empty<Hero>(), null, folder));
        Assert.False(File.Exists(Path.Combine(folder, InsightsJson.FileName)));
    }

    public void Dispose() => TestTemp.Delete(folder);

    /// <summary>Returns the insights it was given, or throws when given none.</summary>
    private sealed class FixedReader : IMatchInsightsReader
    {
        private readonly MatchInsights insights;

        public FixedReader(MatchInsights insights)
        {
            this.insights = insights;
        }

        public MatchInsights Read(
            Replay replay,
            IReadOnlyList<Hero> catalog,
            IReadOnlyDictionary<TimeSpan, Focus> focus = null
        ) => insights ?? throw new InvalidDataException("The replay has no tracker events.");
    }
}
