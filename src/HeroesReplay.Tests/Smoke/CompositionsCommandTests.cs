using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.CLI;
using HeroesReplay.CLI.Commands.Calculators.Commands;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Metadata;
using Xunit;

namespace HeroesReplay.Tests.Smoke;

/// <summary>
/// <c>calculators compositions</c> (#140). The parse-error cases stop before the hero catalog
/// loads. The report case reads the bundled replay against a catalog built from its players.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Smoke)]
public class CompositionsCommandTests
{
    [Fact]
    public async Task Help_ListsTheDirectoryAndOutputOptions()
    {
        (int code, string output, _) = await InvokeAsync("calculators", "compositions", "--help");

        Assert.Equal(0, code);
        Assert.Contains("--directory", output, StringComparison.Ordinal);
        Assert.Contains("--output", output, StringComparison.Ordinal);
        Assert.NotEmpty(
            new HeroesReplay.CLI.Commands.HeroesReplayCommand()
                .Parse("calculators compositions")
                .Errors
        );
    }

    [Fact]
    public async Task MissingDirectory_ExitsOneBeforeLoadingTheCatalog()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            "hr-compositions-" + Guid.NewGuid().ToString("N")
        );

        (int code, _, string error) = await InvokeAsync(
            "calculators",
            "compositions",
            "--directory",
            missing
        );

        Assert.Equal(1, code);
        Assert.Contains("was not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Output_MustBeMarkdownOrCsv()
    {
        string folder = Directory.CreateTempSubdirectory("hr-compositions-").FullName;
        try
        {
            (int code, _, string error) = await InvokeAsync(
                "calculators",
                "compositions",
                "-d",
                folder,
                "-o",
                Path.Combine(folder, "report.txt")
            );

            Assert.Equal(1, code);
            Assert.Contains(".md or .csv", error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Run_ReportsEachReplayAndTheFrequencies()
    {
        string asset = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "hour-long-replay-provided-by-mgatner.StormReplay"
        );
        string folder = Directory.CreateTempSubdirectory("hr-compositions-").FullName;
        try
        {
            string replay = Path.Combine(folder, "65550001.StormReplay");
            File.Copy(asset, replay);
            string markdown = Path.Combine(folder, "report.md");
            string csv = Path.Combine(folder, "report.csv");
            IReadOnlyList<Hero> catalog = CasterCatalog(replay);
            using var output = new StringWriter();
            using var error = new StringWriter();

            int code = CompositionsCommand.Run(
                new[] { replay },
                catalog,
                new YouTubeTitleSettings(),
                markdown,
                output,
                error,
                CancellationToken.None
            );
            int csvCode = CompositionsCommand.Run(
                new[] { replay },
                catalog,
                new YouTubeTitleSettings(),
                csv,
                TextWriter.Null,
                TextWriter.Null,
                CancellationToken.None
            );

            string text = output.ToString();
            Assert.Equal(0, code);
            Assert.Equal(0, csvCode);
            Assert.Contains("[1/1] 65550001", text, StringComparison.Ordinal);
            Assert.Contains("Games with both teams matched: 1", text, StringComparison.Ordinal);
            Assert.Contains("Blue [No tank or healer; Poke]", text, StringComparison.Ordinal);
            Assert.Contains("title: No tank or healer", text, StringComparison.Ordinal);
            Assert.Contains(
                "| Poke | Poke | 2 | 100.0% | 1 | 100.0% | no |",
                File.ReadAllText(markdown)
            );
            Assert.StartsWith(
                "kind,key,label,teams",
                File.ReadAllText(csv),
                StringComparison.Ordinal
            );
            Assert.Contains("composition,Poke,Poke,2,1.0000,1,1.0000,no", File.ReadAllText(csv));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>Every player in the replay as a ranged caster with no tank or healer role.</summary>
    private static IReadOnlyList<Hero> CasterCatalog(string replay)
    {
        (_, Replay parsed) = DataParser.ParseReplay(
            File.ReadAllBytes(replay),
            new ParseOptions
            {
                IgnoreErrors = true,
                ShouldParseEvents = false,
                ShouldParseUnits = false,
                ShouldParseStatistics = false,
            }
        );
        var catalog = new List<Hero>();
        foreach (Player player in parsed.Players)
        {
            catalog.Add(
                new Hero(
                    player.Character,
                    "Hero" + player.HeroAttributeId,
                    player.Character,
                    player.HeroAttributeId,
                    new[] { "RoleCaster" },
                    HeroDraft.RangedAssassin,
                    isMelee: false,
                    ratings: new HeroRatings(5, 5, 5, 5)
                )
            );
        }

        return catalog;
    }

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
