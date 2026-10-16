using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.CLI.Commands.HeroesProfile.Commands;
using HeroesReplay.Core.HeroesProfile;
using Xunit;

namespace HeroesReplay.Tests.Smoke;

/// <summary><c>heroesprofile player-hero</c> (#433): parse errors stop before any network call.</summary>
[Trait(TestCategories.Category, TestCategories.Smoke)]
public class PlayerHeroCommandTests
{
    [Fact]
    public async Task Help_ListsTheOptions()
    {
        (int code, string help, _) = await InvokeAsync("heroesprofile", "player-hero", "--help");

        Assert.Equal(0, code);
        foreach (
            string option in new[] { "--battletag", "--region", "--hero", "--attribute", "--fresh" }
        )
        {
            Assert.Contains(option, help, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("NoNumber", "EU")]
    [InlineData("Name#1234", "PTR")]
    public async Task ABadBattleTagOrRegion_ExitsOne_WithoutACall(string battleTag, string region)
    {
        (int code, _, string error) = await InvokeAsync(
            "heroesprofile",
            "player-hero",
            "--battletag",
            battleTag,
            "--region",
            region,
            "--hero",
            "Valla"
        );

        Assert.Equal(1, code);
        Assert.Contains("--battletag must be", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("na", 1)]
    [InlineData("EU", 2)]
    [InlineData("3", 3)]
    [InlineData("CN", 5)]
    [InlineData("4", 0)]
    public void Region_ReadsANameOrAnId(string text, int region) =>
        Assert.Equal(region, PlayerHeroCommand.Region(text));

    [Fact]
    public void Describe_SaysTheRecordOrWhyThereIsNone()
    {
        string known = PlayerHeroCommand.Describe(
            new PlayerHeroLookup(
                PlayerHeroLookupStatus.Known,
                new PlayerHeroStats(27, 27, 54, 6.12, 2300, "Silver 1"),
                Cached: true
            )
        );
        string hidden = PlayerHeroCommand.Describe(
            new PlayerHeroLookup(PlayerHeroLookupStatus.Private, Reason: "player_unavailable")
        );

        Assert.Equal(
            "Known (cached): 27 wins of 54 games (50.0 %), KDA 6.12, hero MMR 2300, Silver 1.",
            known
        );
        Assert.Equal("Private: player_unavailable", hidden);
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
