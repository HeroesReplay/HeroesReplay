using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.CLI.Commands;
using Xunit;

namespace HeroesReplay.Tests.Smoke;

/// <summary>
/// Exit codes through <see cref="CommandLineService"/>, the same path Program.Main takes.
/// Spectate needs no elevation (#133). Every case here stops at help or a parse error, so
/// nothing launches the game, OBS, or Twitch.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Smoke)]
public class CliExitCodeTests
{
    [Theory]
    [InlineData("spectate --help")]
    [InlineData("spectate file --help")]
    [InlineData("spectate file -h")]
    [InlineData("spectate heroesprofile --help")]
    [InlineData("spectate file --player 11 --help")]
    [InlineData("--help")]
    [InlineData("--version")]
    public async Task HelpAndVersion_ExitZero(string line)
    {
        (int code, string output, string error) = await InvokeAsync(line);

        Assert.Equal(0, code);
        Assert.False(string.IsNullOrWhiteSpace(output));
        Assert.DoesNotContain("administrator", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[suggest] spectate")]
    [InlineData("[suggest] spectate fi")]
    public async Task CommandDiscovery_ExitsZero(string line)
    {
        (int code, _, string error) = await InvokeAsync(line);

        Assert.Equal(0, code);
        Assert.DoesNotContain("administrator", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpectateFileHelp_ListsTheOptions()
    {
        (int code, string output, _) = await InvokeAsync("spectate file --help");

        Assert.Equal(0, code);
        Assert.Contains("--file", output, StringComparison.Ordinal);
        Assert.Contains("--player", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("spectate file --player 11")]
    [InlineData("spectate file --player -1")]
    [InlineData("spectate file --player 1.5")]
    [InlineData("spectate file --player abc")]
    [InlineData("spectate file --player")]
    public async Task InvalidPlayer_ExitsNonZeroWithoutRunning(string line)
    {
        ParseResult parse = new HeroesReplayCommand().Parse(line);
        Assert.NotEmpty(parse.Errors);
        Assert.NotSame(parse.CommandResult.Command.Action, parse.Action);

        (int code, _, string error) = await InvokeAsync(line);

        Assert.NotEqual(0, code);
        Assert.Contains("--player", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1", 0)]
    [InlineData("5", 4)]
    [InlineData("9", 8)]
    [InlineData("10", 9)]
    [InlineData("0", 9)]
    public void ValidPlayer_IsTheZeroBasedHero(string player, int index)
    {
        ParseResult parse = new HeroesReplayCommand().Parse("spectate file --player " + player);

        Assert.Empty(parse.Errors);
        Assert.Equal(index, parse.GetValue<int?>("--player"));
    }

    [Fact]
    public async Task MissingReplayPath_ExitsNonZeroWithoutRunning()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            "hr-missing-" + Guid.NewGuid().ToString("N") + ".StormReplay"
        );
        (int code, _, string error) = await InvokeAsync("spectate file --file " + missing);

        Assert.NotEqual(0, code);
        Assert.Contains("does not exist", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("green")]
    [InlineData("blu")]
    [InlineData("")]
    public void InvalidPredictionOutcome_IsAParseError(string outcome)
    {
        ParseResult parse = new HeroesReplayCommand().Parse(
            new[] { "twitch", "predictions", "test", "--outcome", outcome }
        );

        Assert.NotEmpty(parse.Errors);
        Assert.NotSame(parse.CommandResult.Command.Action, parse.Action);
    }

    [Theory]
    [InlineData("Blue")]
    [InlineData("red")]
    [InlineData("CANCEL")]
    public void PredictionOutcome_AcceptsBlueRedOrCancel(string outcome)
    {
        ParseResult parse = new HeroesReplayCommand().Parse(
            new[] { "twitch", "predictions", "test", "--outcome", outcome }
        );

        Assert.Empty(parse.Errors);
    }

    private static async Task<(int Code, string Output, string Error)> InvokeAsync(string line)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = await new CommandLineService().InvokeAsync(
            line.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            new InvocationConfiguration { Output = output, Error = error }
        );
        return (code, output.ToString(), error.ToString());
    }
}
