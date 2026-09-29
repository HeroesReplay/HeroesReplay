using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientScreenTextTests
{
    [Fact]
    public void IsLoginForm_MatchesTheBattleNetEmailForm()
    {
        const string text = "Email or Phone Password Keep me logged in Log in Battle.net Account";

        Assert.True(ClientScreenText.IsLoginForm(text));
    }

    [Fact]
    public void IsLoginForm_IgnoresThePlayMenu()
    {
        Assert.False(ClientScreenText.IsLoginForm("PLAY COLLECTION LOOT WATCH"));
    }

    [Fact]
    public void IsVersionMismatch_MatchesTheRegionDialog()
    {
        Assert.True(
            ClientScreenText.IsVersionMismatch(
                "VERSION MISMATCH Game client version mismatch with selected region."
            )
        );
        Assert.False(ClientScreenText.IsVersionMismatch("PLAY COLLECTION LOOT WATCH"));
    }

    [Fact]
    public void IsRegionUnavailable_MatchesTheRegionDialog()
    {
        Assert.True(
            ClientScreenText.IsRegionUnavailable(
                "ERROR The selected region is currently unavailable. Please try again later or select another region."
            )
        );
    }

    [Fact]
    public void IsRegionUnavailable_IgnoresTheVersionMismatchDialog()
    {
        Assert.False(
            ClientScreenText.IsRegionUnavailable(
                "VERSION MISMATCH Game client version mismatch with selected region."
            )
        );
    }

    [Fact]
    public void IsRegionUnavailable_IgnoresThePlayMenu()
    {
        Assert.False(ClientScreenText.IsRegionUnavailable("PLAY COLLECTION LOOT WATCH"));
        Assert.False(ClientScreenText.IsRegionUnavailable(null));
        Assert.False(ClientScreenText.IsRegionUnavailable("region"));
        Assert.False(ClientScreenText.IsRegionUnavailable("unavailable"));
    }
}
