using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using Xunit;

namespace HeroesReplay.Tests.Smoke;

/// <summary><c>youtube preview</c> (#412). The error case stops before anything loads.</summary>
[Trait(TestCategories.Category, TestCategories.Smoke)]
public class YouTubePreviewCommandTests
{
    [Fact]
    public async Task Help_ListsTheOptions_AndAMissingReplayExitsOne()
    {
        (int help, string output, _) = await InvokeAsync("youtube", "preview", "--help");
        (int missing, _, string error) = await InvokeAsync(
            "youtube",
            "preview",
            "--replay",
            Path.Combine(
                Path.GetTempPath(),
                "hr-preview-" + Guid.NewGuid().ToString("N") + ".StormReplay"
            )
        );

        Assert.Equal(0, help);
        Assert.Contains("--replay", output, StringComparison.Ordinal);
        Assert.Contains("--configured", output, StringComparison.Ordinal);
        Assert.Contains("--insights", output, StringComparison.Ordinal);
        Assert.Equal(1, missing);
        Assert.Contains("was not found", error, StringComparison.Ordinal);
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
