using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace HeroesReplay.Tests.Unit.Configuration;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReportScenesTests
{
    private static readonly string[] ExpectedScenes =
    {
        "summary",
        "match-scores",
        "talents",
        "experience",
        "team-1-stats",
        "team-2-stats",
    };

    [Fact]
    public void AppSettings_EnablesHeroesProfileReportScenes()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        Assert.True(File.Exists(path), $"expected {path} to be copied to the test output.");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement scenes = document.RootElement.GetProperty("OBS").GetProperty("ReportScenes");

        var names = new List<string>();
        foreach (JsonElement scene in scenes.EnumerateArray())
        {
            Assert.True(
                scene.GetProperty("Enabled").GetBoolean(),
                $"{scene.GetProperty("SceneName").GetString()} should be enabled."
            );
            string url = scene.GetProperty("SourceUrl").GetString();
            Assert.Contains("/Match/Single/[ID]", url, StringComparison.Ordinal);
            names.Add(scene.GetProperty("SceneName").GetString());
        }

        Assert.Equal(ExpectedScenes, names);
        Assert.Equal(ExpectedScenes.Length, names.Distinct().Count());
    }
}
