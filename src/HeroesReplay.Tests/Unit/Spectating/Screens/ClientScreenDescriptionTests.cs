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
    public void Excerpt_IsOneLineOfAtMost160Characters()
    {
        Assert.Equal("(empty)", WindowText.Excerpt("  "));
        Assert.Equal("(empty)", WindowText.Excerpt(null));
        Assert.Equal("PLAY COLLECTION", WindowText.Excerpt("PLAY\nCOLLECTION "));
        string excerpt = WindowText.Excerpt(new string('a', 400));
        Assert.Equal(new string('a', WindowText.ExcerptLength) + "...", excerpt);
    }
}
