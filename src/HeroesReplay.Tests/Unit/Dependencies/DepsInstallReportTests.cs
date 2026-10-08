using System.Text.Json;
using HeroesReplay.CLI.Commands.Deps;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.Dependencies;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.Dependencies;

/// <summary><c>deps install --output json</c> (#311).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class DepsInstallReportTests
{
    private const string Root = @"C:\heroesreplay\tools";

    [Fact]
    public void AlreadyInstalled_IsOk()
    {
        CliResult<DepsInstallDetails> report = DepsCommand.Report(
            Root,
            [Tool(DependencyInstallOutcome.AlreadyInstalled, "ffmpeg 9.0.2 is already installed.")],
            null
        );

        Assert.True(report.Ok);
        Assert.Equal("deps.already_installed", report.Code);
        Assert.Equal(0, CliOutput.ExitCode(report));
    }

    [Fact]
    public void ADownload_IsInstalled()
    {
        CliResult<DepsInstallDetails> report = DepsCommand.Report(
            Root,
            [Tool(DependencyInstallOutcome.Installed, "ffmpeg 9.0.2 installed.")],
            "Clips look in C:\\other, not C:\\heroesreplay\\tools."
        );

        Assert.True(report.Ok);
        Assert.Equal("deps.installed", report.Code);
        Assert.StartsWith("Clips look in", report.Details.Note);
    }

    [Fact]
    public void AFailure_ExitsOneWithItsMessage()
    {
        CliResult<DepsInstallDetails> report = DepsCommand.Report(
            Root,
            [
                Tool(
                    DependencyInstallOutcome.Failed,
                    "ffmpeg 9.0.2 was not installed: SHA-256 mismatch."
                ),
            ],
            null
        );

        Assert.False(report.Ok);
        Assert.Equal("deps.failed", report.Code);
        Assert.Equal("ffmpeg 9.0.2 was not installed: SHA-256 mismatch.", report.Message);
        Assert.Equal(1, CliOutput.ExitCode(report));
    }

    [Fact]
    public void TheJson_NamesEachToolAndItsOutcome()
    {
        CliResult<DepsInstallDetails> report = DepsCommand.Report(
            Root,
            [Tool(DependencyInstallOutcome.AlreadyInstalled, "done")],
            null
        );

        using JsonDocument json = JsonDocument.Parse(CliJson.Serialize(report));
        JsonElement tool = json.RootElement.GetProperty("details").GetProperty("tools")[0];
        Assert.Equal("ffmpeg", tool.GetProperty("name").GetString());
        Assert.Equal("alreadyInstalled", tool.GetProperty("outcome").GetString());
        Assert.Equal(
            Root,
            json.RootElement.GetProperty("details").GetProperty("directory").GetString()
        );
    }

    private static DepsToolResult Tool(DependencyInstallOutcome outcome, string message) =>
        new("ffmpeg", "9.0.2", outcome, outcome != DependencyInstallOutcome.Failed, message);
}
