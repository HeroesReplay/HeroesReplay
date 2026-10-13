using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.CLI.Commands.Builds.Commands;
using HeroesReplay.Core.Builds.Import;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.Shared;
using HeroesReplay.Tests.Unit.Builds;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Smoke;

/// <summary>
/// <c>builds import</c> and <c>builds match</c> (#412). The parse-error cases stop before the hero
/// catalog loads; the import cases run against a catalog of one test hero.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Smoke)]
public class BuildsCommandTests
{
    [Fact]
    public async Task Help_ListsTheOptions()
    {
        (int import, string importHelp, _) = await InvokeAsync("builds", "import", "--help");
        (int match, string matchHelp, _) = await InvokeAsync("builds", "match", "--help");

        Assert.Equal(0, import);
        Assert.Contains("--input", importHelp, StringComparison.Ordinal);
        Assert.Contains("--reference", importHelp, StringComparison.Ordinal);
        Assert.Contains("--checked-at", importHelp, StringComparison.Ordinal);
        Assert.Equal(0, match);
        Assert.Contains("--replay", matchHelp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingFiles_ExitOneBeforeLoadingTheCatalog()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            "hr-builds-" + Guid.NewGuid().ToString("N")
        );

        (int import, _, string importError) = await InvokeAsync(
            "builds",
            "import",
            "--input",
            missing + ".json",
            "--reference",
            missing + "-out.json"
        );
        (int match, _, string matchError) = await InvokeAsync(
            "builds",
            "match",
            "--replay",
            missing + ".StormReplay"
        );

        Assert.Equal(1, import);
        Assert.Contains("was not found", importError, StringComparison.Ordinal);
        Assert.Equal(1, match);
        Assert.Contains("was not found", matchError, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_WritesTheReference_OrRefusesAndWritesNothing()
    {
        string folder = Directory.CreateTempSubdirectory("hr-builds-").FullName;
        try
        {
            string reference = Path.Combine(folder, "icy-veins-builds.json");
            Hero[] catalog = { BuildFixtures.Hero() };
            using var console = new StringWriter();
            using var error = new StringWriter();

            int refused = ImportCommand.Run(
                new[] { Page("nobody") },
                catalog,
                new DateOnly(2026, 10, 12),
                "2.57.0.98304",
                reference,
                console,
                error
            );

            Assert.Equal(2, refused);
            Assert.False(File.Exists(reference));
            Assert.Contains("names no hero", error.ToString(), StringComparison.Ordinal);

            int written = ImportCommand.Run(
                new[] { Page("li-ming") },
                catalog,
                new DateOnly(2026, 10, 12),
                "2.57.0.98304",
                reference,
                console,
                error
            );

            Assert.Equal(0, written);
            GuideReference read = GuideReferenceJson.Parse(File.ReadAllText(reference));
            Assert.Equal("Teleport Build", Assert.Single(Assert.Single(read.Heroes).Builds).Name);
            Assert.Contains(
                "Wrote 1 heroes and 1 builds",
                console.ToString(),
                StringComparison.Ordinal
            );
        }
        finally
        {
            TestTemp.Delete(folder);
        }
    }

    private static GuideExtractHero Page(string slug) =>
        new()
        {
            Slug = slug,
            Updated = "Oct 27, 2020",
            Builds = new[]
            {
                new GuideExtractBuild
                {
                    Name = "Teleport Build",
                    Tag = "Recommended",
                    Tiers = new[] { "ynn", "ynn", "ynn", "yn", "ynn", "ynn", "ynnn" }
                        .Select(marks => new GuideExtractTier { Marks = marks })
                        .ToArray(),
                },
            },
        };

    private static async Task<(int Code, string Output, string Error)> InvokeAsync(
        params string[] args
    )
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = await new CommandLineService().InvokeAsync(
            args,
            new InvocationConfiguration { Output = output, Error = error }
        );
        return (code, output.ToString(), error.ToString());
    }
}
