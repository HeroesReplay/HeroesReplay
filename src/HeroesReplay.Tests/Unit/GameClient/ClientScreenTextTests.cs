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
    public void IsVersionMismatch_MatchesTheBuildNotAvailableDialog()
    {
        // OCR of the dialog Blizzard shows for build 2.57.0.98297 on ASA-SERVER, 2026-10-07.
        const string Ocr =
            "The version of Heroes of the Storm required to ploy this game is not available. 0K";
        Assert.True(ClientScreenText.IsVersionNotAvailable(Ocr));
        Assert.True(ClientScreenText.IsVersionMismatch(Ocr));
        Assert.False(
            ClientScreenText.IsVersionNotAvailable("Preparing game data Calculating... Cancel")
        );
        Assert.False(ClientScreenText.IsVersionNotAvailable("OCR is not available"));
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
