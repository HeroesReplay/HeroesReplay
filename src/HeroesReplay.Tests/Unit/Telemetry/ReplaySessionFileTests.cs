using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HeroesReplay.Core.Telemetry;
using Xunit;

namespace HeroesReplay.Tests.Unit.Telemetry;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplaySessionFileTests
{
    [Fact]
    public void Publish_KeepsOnlyTheNewestSessions()
    {
        // Nothing removed sessions before: production had 395 after a week, read every 2 seconds.
        string directory = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-session-file-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "replay-sessions.txt");
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == HeroesReplayTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        int first = 626000;
        int count = ReplaySessionFile.MaxSessions + 5;
        try
        {
            for (int id = first; id < first + count; id++)
            {
                using Activity session = HeroesReplayTelemetry.BeginReplaySession(id);
                ReplaySessionFile.Publish(session, id, path);
            }

            // Publishing a known replay again moves it to the end instead of adding a line.
            using (Activity again = HeroesReplayTelemetry.BeginReplaySession(first + 10))
            {
                ReplaySessionFile.Publish(again, first + 10, path);
            }

            List<int> ids = ReplaySessionFile.ReadIds(path);

            Assert.Equal(ReplaySessionFile.MaxSessions, ids.Count);
            Assert.Equal(ReplaySessionFile.MaxSessions, File.ReadAllLines(path).Length);
            Assert.DoesNotContain(first, ids);
            Assert.DoesNotContain(first + 4, ids);
            Assert.Equal(first + 5, ids[0]);
            Assert.Equal(first + 10, ids[^1]);
            Assert.Contains(first + count - 1, ids);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
