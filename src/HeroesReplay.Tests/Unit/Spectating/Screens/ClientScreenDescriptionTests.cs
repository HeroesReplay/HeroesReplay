using HeroesClientSDK;
using HeroesReplay.Core.Spectating.Screens;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Screens;

/// <summary>The log line for a HeroesClientSDK read, from samples read on 2026-10-08.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientScreenDescriptionTests
{
    private static readonly HeroesClientVersion Current = new(2, 57, 0, 98348);

    [Fact]
    public void Describe_NamesTheScreenReasonShownScreensMenuSeenDialogsLaunchResultAndBuild()
    {
        var login = new ClientScreenSample(
            ClientScreenKind.Login,
            new[]
            {
                "ScreenBackgroundHero",
                "ScreenLoginUnified",
                "ScreenHeroCutscene",
                "ScreenNavigationHero",
                "ScreenForegroundHero",
            },
            MenuSeen: true,
            "screens",
            Current
        );
        var versionDialog = new ClientScreenSample(
            ClientScreenKind.Dialog,
            new[] { "ScreenBackgroundHero", "ScreenLoginUnified" },
            MenuSeen: true,
            "screens",
            Current,
            false,
            Dialogs: new[] { "CStandardDialog" },
            LaunchResultCode: 23,
            LaunchResult: "GameLaunchUnsupportedNoData"
        );

        Assert.Equal(
            "Login, screens, shown [BackgroundHero,LoginUnified,HeroCutscene,NavigationHero,ForegroundHero], menu seen True, build 2.57.0.98348",
            ClientScreenDescription.Describe(login)
        );
        Assert.Equal(
            "Dialog, screens, shown [BackgroundHero,LoginUnified], menu seen True, dialogs [CStandardDialog], launch result 23 GameLaunchUnsupportedNoData, build 2.57.0.98348",
            ClientScreenDescription.Describe(versionDialog)
        );
        Assert.Equal("not read", ClientScreenDescription.Describe(null));
    }

    [Fact]
    public void Describe_NamesABattlenetErrorDialogWithItsText()
    {
        // HeroesClientSDK 0.4.4 reads the dialog's labels from memory; never reproduced live, so
        // the text is the client's own table entry (Battle.net error 169, 2.57.0.98348).
        var region = new ClientScreenSample(
            ClientScreenKind.Dialog,
            new[] { "ScreenLoginUnified" },
            MenuSeen: true,
            "screens",
            Current,
            false,
            Dialogs: new[] { "CBattlenetErrorDialog" },
            DialogMessages: new[]
            {
                new DialogMessage(
                    "CBattlenetErrorDialog",
                    "Error",
                    "The selected region is currently unavailable. Please try again later or select another region."
                ),
            }
        );
        var unread = region with
        {
            DialogMessages = new[] { new DialogMessage("CBattlenetErrorDialog", null, null) },
        };

        Assert.Equal(
            "Dialog, screens, shown [LoginUnified], menu seen True, dialogs [CBattlenetErrorDialog], CBattlenetErrorDialog \"Error The selected region is currently unavailable. Please try again later or select another region.\", build 2.57.0.98348",
            ClientScreenDescription.Describe(region)
        );
        Assert.EndsWith(
            "CBattlenetErrorDialog (text not read), build 2.57.0.98348",
            ClientScreenDescription.Describe(unread)
        );
    }
}
