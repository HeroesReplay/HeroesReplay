using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientScreenTextTests
{
    [Fact]
    public void IsGameDataStartup_MatchesThePreparingDialog()
    {
        Assert.True(ClientScreenText.IsGameDataStartup("Preparing game data"));
        Assert.True(ClientScreenText.IsGameDataStartup("PREPARING GAME DATA Calculating"));
        Assert.False(ClientScreenText.IsGameDataStartup("PLAY COLLECTION LOOT WATCH"));
        Assert.False(ClientScreenText.IsGameDataStartup("Calculating"));
        Assert.False(ClientScreenText.IsGameDataStartup(null));
        Assert.False(ClientScreenText.IsGameDataStartup("   "));
    }

    [Fact]
    public void IsGameDataDownload_MatchesTheVersionDataDialog()
    {
        Assert.True(
            ClientScreenText.IsGameDataDownload(
                "DOWNLOADING All data files must be fully downloaded to load this version of the game. Calculating... CANCEL"
            )
        );
        Assert.False(ClientScreenText.IsGameDataDownload("Preparing game data"));
        Assert.False(ClientScreenText.IsGameDataDownload("DOWNLOADING"));
        Assert.False(ClientScreenText.IsGameDataDownload(null));
        Assert.False(ClientScreenText.IsGameDataDownload("   "));
    }

    [Fact]
    public void IsGameDataDownload_LaterSampleLeavesTheClientRunning()
    {
        const string dialog =
            "DOWNLOADING All data files must be fully downloaded to load this version of the game. CANCEL";

        Assert.True(ClientScreenText.IsGameDataDownload(string.Empty, dialog));
        Assert.True(ClientScreenText.IsGameDataDownload("Preparing game data", dialog));
        Assert.False(ClientScreenText.IsGameDataDownload(string.Empty, "Preparing game data"));
        Assert.False(ClientScreenText.IsGameDataDownload(null, null));
        Assert.True(ClientScreenText.IsGameDataStartup(string.Empty, "Preparing game data"));
        Assert.False(
            ClientInterfacePlan.RestartAfterGameData(
                sawDownload: ClientScreenText.IsGameDataDownload(string.Empty, dialog),
                downloadVisible: ClientScreenText.IsGameDataDownload(string.Empty, dialog),
                gameDataStartup: ClientScreenText.IsGameDataStartup(string.Empty, dialog),
                replayVisible: false,
                restarts: 0
            )
        );
        Assert.False(
            ClientInterfacePlan.RestartAfterGameData(
                sawDownload: ClientScreenText.IsGameDataDownload(string.Empty, dialog),
                downloadVisible: ClientScreenText.IsGameDataDownload(
                    string.Empty,
                    "Preparing game data"
                ),
                gameDataStartup: ClientScreenText.IsGameDataStartup(
                    string.Empty,
                    "Preparing game data"
                ),
                replayVisible: false,
                restarts: 0
            )
        );
    }

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
