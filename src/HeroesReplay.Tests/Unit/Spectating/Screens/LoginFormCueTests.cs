using HeroesReplay.Core.Spectating.Screens;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Screens;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class LoginFormCueTests
{
    [Fact]
    public void Sees_TheFormMemoryReads()
    {
        // 2.57.0.98348 started by HeroesSwitcher without SSO: ScreenLoginUnified with no dialog.
        Assert.True(LoginFormCue.Sees(loginInMemory: true));
    }

    [Fact]
    public void Sees_NotTheFormWhenMemorySaysOtherwiseOrCannotTell()
    {
        // Battle.net's AUTHENTICATION panel reads Authenticating, not Login (0.4.1).
        Assert.False(LoginFormCue.Sees(loginInMemory: false));
        Assert.False(LoginFormCue.Sees(loginInMemory: null));
    }
}
