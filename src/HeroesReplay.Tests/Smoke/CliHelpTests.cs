using System.CommandLine;
using System.Linq;
using HeroesReplay.CLI.Commands;
using Xunit;

namespace HeroesReplay.Tests.Smoke;

[Trait(TestCategories.Category, TestCategories.Smoke)]
public class CliHelpTests
{
    [Fact]
    public void RootHelp_HasCheckAndSpectate()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("--help");
        Assert.Empty(result.Errors);
        Assert.Contains(root.Subcommands, c => c.Name == "check");
        Assert.Contains(root.Subcommands, c => c.Name == "spectate");
        Assert.Contains(root.Subcommands, c => c.Name == "calculators");
        Assert.Contains(root.Subcommands, c => c.Name == "mcp");
        Assert.Contains(root.Subcommands, c => c.Name == "client");
        Assert.Contains(root.Subcommands, c => c.Name == "otel");
    }

    [Fact]
    public void CheckHelp_ListsServiceTargets()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("check --help");
        Assert.Empty(result.Errors);
        Command check = root.Subcommands.Single(c => c.Name == "check");
        Assert.Contains(check.Subcommands, c => c.Name == "config");
        Assert.Contains(check.Subcommands, c => c.Name == "heroesprofile");
        Assert.Contains(check.Subcommands, c => c.Name == "obs");
        Assert.Contains(check.Subcommands, c => c.Name == "twitch");
        Assert.Contains(check.Subcommands, c => c.Name == "client");
        Assert.Contains(check.Subcommands, c => c.Name == "connectivity");
        Assert.Contains(check.Subcommands, c => c.Name == "timer");
    }

    [Fact]
    public void ClientHelp_HasConfigureAndStatus()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("client --help");
        Assert.Empty(result.Errors);
        Command client = root.Subcommands.Single(c => c.Name == "client");
        Assert.Contains(client.Subcommands, c => c.Name == "configure");
        Assert.Contains(client.Subcommands, c => c.Name == "status");
    }

    [Fact]
    public void OtelHelp_HasUpDownStatus()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("otel --help");
        Assert.Empty(result.Errors);
        Command otel = root.Subcommands.Single(c => c.Name == "otel");
        Assert.Contains(otel.Subcommands, c => c.Name == "up");
        Assert.Contains(otel.Subcommands, c => c.Name == "down");
        Assert.Contains(otel.Subcommands, c => c.Name == "status");
    }

    [Fact]
    public void ServicesHelp_HasStartStopStatus()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("services --help");
        Assert.Empty(result.Errors);
        Command services = root.Subcommands.Single(c => c.Name == "services");
        Assert.Contains(services.Subcommands, c => c.Name == "start");
        Assert.Contains(services.Subcommands, c => c.Name == "stop");
        Assert.Contains(services.Subcommands, c => c.Name == "status");
    }

    [Fact]
    public void HeroesProfileHelp_HasDownload()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("heroesprofile --help");
        Assert.Empty(result.Errors);
        Command heroesProfile = root.Subcommands.Single(c => c.Name == "heroesprofile");
        Assert.Contains(heroesProfile.Subcommands, c => c.Name == "download");
    }

    [Fact]
    public void TwitchHelp_HasPredictionsTest()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("twitch --help");
        Assert.Empty(result.Errors);
        Command twitch = root.Subcommands.Single(c => c.Name == "twitch");
        Command predictions = twitch.Subcommands.Single(c => c.Name == "predictions");
        Assert.Contains(predictions.Subcommands, c => c.Name == "test");
        Command rewards = twitch.Subcommands.Single(c => c.Name == "rewards");
        Assert.Contains(rewards.Subcommands, c => c.Name == "list");
        Assert.Contains(rewards.Subcommands, c => c.Name == "test");
    }

    [Fact]
    public void CalculatorsHelp_HasUnitsReport()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("calculators units --help");
        Assert.Empty(result.Errors);
        Command calculators = root.Subcommands.Single(c => c.Name == "calculators");
        Assert.Contains(calculators.Subcommands, c => c.Name == "report");
        Assert.Contains(calculators.Subcommands, c => c.Name == "coordinates");
        Assert.Contains(calculators.Subcommands, c => c.Name == "units");
    }
}
