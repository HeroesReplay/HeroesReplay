using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.Obs;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsVersionLabelTests
{
    [Theory]
    [InlineData("v1.0.0-614", "1.0.0+e6141f861a9d20a05e93c5f97832f51e77fa33fe", "v1.0.0-614")]
    [InlineData(" v1.0.0-614 ", null, "v1.0.0-614")]
    [InlineData("", "1.0.0+e6141f861a9d20a05e93c5f97832f51e77fa33fe", "dev e6141f8")]
    [InlineData(null, "1.0.0+abc", "dev abc")]
    [InlineData(null, "1.0.0", "dev")]
    [InlineData(null, null, "dev")]
    public void Text_IsTheReleaseTagOrTheSourceCommit(
        string release,
        string informational,
        string expected
    )
    {
        Assert.Equal(expected, ObsVersionLabel.Text(release, informational));
    }

    [Fact]
    public void WriteForThisInstall_PutsTheLabelInTheDataDirectory()
    {
        string data = Path.Combine(Path.GetTempPath(), "hr-version-" + Path.GetRandomFileName());
        try
        {
            string text = ObsVersionLabel.WriteForThisInstall(data);

            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.Equal(text, File.ReadAllText(Path.Combine(data, ObsVersionLabel.FileName)));
        }
        finally
        {
            if (Directory.Exists(data))
            {
                Directory.Delete(data, recursive: true);
            }
        }
    }

    [Fact]
    public void Template_ShowsTheLabelBottomRightOnTheWaitingSceneOnly()
    {
        using JsonDocument template = JsonDocument.Parse(File.ReadAllText(FindTemplate()));
        JsonElement[] sources = template
            .RootElement.GetProperty("sources")
            .EnumerateArray()
            .ToArray();
        JsonElement label = sources.Single(source =>
            source.GetProperty("name").GetString() == "release-version"
        );
        JsonElement settings = label.GetProperty("settings");
        Assert.True(settings.GetProperty("read_from_file").GetBoolean());
        Assert.Equal(
            "C:/heroesreplay/Data/" + ObsVersionLabel.FileName,
            settings.GetProperty("file").GetString()
        );

        string[] scenesWithLabel = sources
            .Where(source => source.GetProperty("id").GetString() == "scene")
            .Where(scene =>
                scene
                    .GetProperty("settings")
                    .GetProperty("items")
                    .EnumerateArray()
                    .Any(item => item.GetProperty("name").GetString() == "release-version")
            )
            .Select(scene => scene.GetProperty("name").GetString())
            .ToArray();
        Assert.Equal(new[] { "waiting-screen" }, scenesWithLabel);

        JsonElement item = sources
            .Single(source => source.GetProperty("name").GetString() == "waiting-screen")
            .GetProperty("settings")
            .GetProperty("items")
            .EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "release-version");
        // Bottom-right anchor (right 2 + bottom 8) near the canvas corner.
        Assert.Equal(10, item.GetProperty("align").GetInt32());
        Assert.True(item.GetProperty("pos").GetProperty("x").GetDouble() > 1800);
        Assert.True(item.GetProperty("pos").GetProperty("y").GetDouble() > 1000);
    }

    private static string FindTemplate()
    {
        for (
            DirectoryInfo directory = new(AppContext.BaseDirectory);
            directory != null;
            directory = directory.Parent
        )
        {
            string candidate = Path.Combine(directory.FullName, "obs", "Default.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("obs/Default.json was not found above the test output.");
    }
}
