using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientScreenTextTests
{
    [Fact]
    public void RestartAfterGameData_ADownloadStillOnScreenLeavesTheClientRunning()
    {
        // The DOWNLOADING dialog comes from memory (a shown CProgressBarDialog, #292): while it
        // shows, or while "Preparing game data" shows, the client is not restarted.
        Assert.False(
            ClientInterfacePlan.RestartAfterGameData(
                sawDownload: true,
                downloadVisible: true,
                gameDataStartup: false,
                replayVisible: false,
                restarts: 0
            )
        );
        Assert.False(
            ClientInterfacePlan.RestartAfterGameData(
                sawDownload: true,
                downloadVisible: false,
                gameDataStartup: true,
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
}
