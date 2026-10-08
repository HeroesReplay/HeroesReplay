using HeroesClientSDK;
using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

/// <summary>
/// Game-launch failures from client memory (#292). The samples are what HeroesClientSDK read on
/// ASA-SERVER on 2026-10-08.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientLaunchFailureTests
{
    private static readonly HeroesClientVersion Current = new(2, 57, 0, 98348);

    // 2.57.0.98348 asked by HeroesSwitcher for a 2.57.0.98297 replay: "The version of Heroes of
    // the Storm required to play this game is not available." in a CStandardDialog over the login
    // screen, launch result 23, launch state 1.
    private static readonly ClientScreenSample NotAvailable = new(
        ClientScreenKind.Dialog,
        new[] { "ScreenBackgroundHero", "ScreenLoginUnified" },
        MenuSeen: true,
        "screens",
        Current,
        false,
        Dialogs: new[] { "CStandardDialog" },
        LaunchResultCode: 23,
        LaunchResult: "GameLaunchUnsupportedNoData",
        LaunchState: 1
    );

    // The same dialog a moment earlier, over the boot splash (mask 0x20).
    private static readonly ClientScreenSample NotAvailableOverSplash = NotAvailable with
    {
        Shown = new[] { "ScreenLoading" },
        MenuSeen = false,
    };

    private static readonly ClientScreenSample Home = new(
        ClientScreenKind.Home,
        new[] { "ScreenHome" },
        MenuSeen: true,
        "screens",
        Current
    );

    [Fact]
    public void Read_TheVersionNotAvailableDialog_IsAFailureWithItsKey()
    {
        LaunchFailure? failure = ClientLaunchFailure.Read(NotAvailable);

        Assert.Equal(new LaunchFailure(23, "GameLaunchUnsupportedNoData"), failure);
        Assert.Equal("23 GameLaunchUnsupportedNoData", failure.ToString());
        Assert.Equal(ClientHoldReason.VersionMismatch, ClientLaunchFailure.Classify(NotAvailable));
        Assert.Equal(
            ClientHoldReason.VersionMismatch,
            ClientLaunchFailure.Classify(NotAvailableOverSplash)
        );
    }

    [Theory]
    [InlineData(2, "GameLaunchReplayOpenFailure")]
    [InlineData(15, "GameLaunchDataBuildNumMismatch")]
    [InlineData(10, "GameLaunchBaseBuildMissing")]
    [InlineData(24, "GameLaunchUnsupportedTooOld")]
    public void Classify_AnyFailureResult_IsTheInvalidClientHold(int code, string key)
    {
        ClientScreenSample sample = NotAvailable with
        {
            LaunchResultCode = code,
            LaunchResult = key,
        };

        Assert.Equal(ClientHoldReason.VersionMismatch, ClientLaunchFailure.Classify(sample));
        Assert.True(ClientLaunchFailure.ShowsVersion(sample));
    }

    [Fact]
    public void Read_TheDownloadingMessageIsNotAFailure()
    {
        // Result 12, GameLaunchVersionDownloadMessage, is "All data files must be fully
        // downloaded to load this version of the game." (2.57.0.98348 string table).
        ClientScreenSample downloading = NotAvailable with
        {
            LaunchResultCode = 12,
            LaunchResult = "GameLaunchVersionDownloadMessage",
        };

        Assert.Null(ClientLaunchFailure.Read(downloading));
        Assert.Equal(ClientHoldReason.None, ClientLaunchFailure.Classify(downloading));
    }

    [Fact]
    public void Read_TheRegionResultIsTheSameInvalidClient()
    {
        // GameLaunchUnsupportedInCN (20): "Replays and saved games created before version 1.3.0
        // are not supported in this region." The only region result in the client's table.
        ClientScreenSample region = NotAvailable with
        {
            LaunchResultCode = 20,
            LaunchResult = "GameLaunchUnsupportedInCN",
        };

        Assert.True(ClientLaunchFailure.Read(region)?.IsRegion);
        Assert.False(ClientLaunchFailure.Read(NotAvailable)?.IsRegion);
        Assert.Equal(ClientHoldReason.VersionMismatch, ClientLaunchFailure.Classify(region));
    }

    [Fact]
    public void Read_NoFailureOnScreen_IsNull()
    {
        ClientScreenSample cleared = NotAvailable with
        {
            LaunchResultCode = 0,
            LaunchResult = null,
        };
        ClientScreenSample otherDialog = NotAvailable with
        {
            Dialogs = new[] { "CDisconnectedDialog" },
        };
        ClientScreenSample download = NotAvailable with
        {
            Screen = ClientScreenKind.Download,
            Dialogs = new[] { "CProgressBarDialog" },
        };
        ClientScreenSample unknown = new(
            ClientScreenKind.Unknown,
            new string[0],
            MenuSeen: false,
            "no-state"
        );

        Assert.Null(ClientLaunchFailure.Read(cleared));
        Assert.Null(ClientLaunchFailure.Read(otherDialog));
        Assert.Null(ClientLaunchFailure.Read(download));
        Assert.Null(ClientLaunchFailure.Read(Home));
        Assert.Null(ClientLaunchFailure.Read(unknown));
        Assert.Null(ClientLaunchFailure.Read(null));
        Assert.Equal(ClientHoldReason.None, ClientLaunchFailure.Classify(Home));
        Assert.Equal(ClientHoldReason.None, ClientLaunchFailure.Classify(null));
    }

    [Fact]
    public void ShowsVersion_IsFalseOnOtherScreensAndUnknownWhenMemoryCannotTell()
    {
        Assert.True(ClientLaunchFailure.ShowsVersion(NotAvailable));
        Assert.False(ClientLaunchFailure.ShowsVersion(Home));
        Assert.Null(
            ClientLaunchFailure.ShowsVersion(
                new ClientScreenSample(
                    ClientScreenKind.Unknown,
                    new string[0],
                    MenuSeen: false,
                    "starting"
                )
            )
        );
        Assert.Null(ClientLaunchFailure.ShowsVersion(null));
        Assert.False(ClientLaunchFailure.ShowsRegion(NotAvailable));
    }
}
