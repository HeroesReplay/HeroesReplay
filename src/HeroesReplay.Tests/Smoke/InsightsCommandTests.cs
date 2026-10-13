using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Smoke;

/// <summary><c>calculators insights</c> (#412). The parse-error cases stop before the hero catalog loads.</summary>
[Trait(TestCategories.Category, TestCategories.Smoke)]
public class InsightsCommandTests
{
    [Fact]
    public async Task Help_ListsTheOptions()
    {
        (int code, string output, _) = await InvokeAsync("calculators", "insights", "--help");

        Assert.Equal(0, code);
        Assert.Contains("--directory", output, StringComparison.Ordinal);
        Assert.Contains("--recursive", output, StringComparison.Ordinal);
        Assert.Contains("--output", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingFolderAnOutputThatIsNotMarkdownAndAnEmptyFolder_ExitOne()
    {
        string folder = Directory.CreateTempSubdirectory("hr-insights-").FullName;
        try
        {
            (int missing, _, string missingError) = await InvokeAsync(
                "calculators",
                "insights",
                "-d",
                Path.Combine(folder, "nope")
            );
            (int csv, _, string csvError) = await InvokeAsync(
                "calculators",
                "insights",
                "-d",
                folder,
                "-o",
                Path.Combine(folder, "report.csv")
            );
            (int empty, _, string emptyError) = await InvokeAsync(
                "calculators",
                "insights",
                "-d",
                folder
            );

            Assert.Equal(1, missing);
            Assert.Contains("was not found", missingError, StringComparison.Ordinal);
            Assert.Equal(1, csv);
            Assert.Contains(".md", csvError, StringComparison.Ordinal);
            Assert.Equal(1, empty);
            Assert.Contains("No .StormReplay files", emptyError, StringComparison.Ordinal);
        }
        finally
        {
            TestTemp.Delete(folder);
        }
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
