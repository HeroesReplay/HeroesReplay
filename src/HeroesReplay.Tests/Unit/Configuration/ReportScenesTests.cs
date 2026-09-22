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
        "prediction-report",
        "match-report",
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
            string name = scene.GetProperty("SceneName").GetString();
            string url = scene.GetProperty("SourceUrl").GetString();
            if (name == "prediction-report")
            {
                Assert.EndsWith("prediction-report.html", url, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.Equal(
                    "https://www.heroesprofile.com/Match/Single/[ID]",
                    url,
                    StringComparer.Ordinal
                );
            }

            names.Add(name);
        }

        Assert.Equal(ExpectedScenes, names);
        Assert.Equal(ExpectedScenes.Length, names.Distinct().Count());
    }
}
