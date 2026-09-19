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
}
