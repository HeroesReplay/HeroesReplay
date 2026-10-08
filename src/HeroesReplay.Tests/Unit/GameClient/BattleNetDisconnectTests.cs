using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Spectating.Screens;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class BattleNetDisconnectTests
{
    // OCR of the email and password form, 2.57.0.98348 started by HeroesSwitcher without SSO
    // (#292 shadow proof, 2026-10-08). It says "Battle.net" and "Log in".
    private const string LoginFormText =
        "Email or Phone Password Keep me logged in Log in Battle.net Account";

    [Theory]
    [InlineData("You have been disconnected from Battle.net")]
    [InlineData("Connection to Battle.net has been lost. Reconnect")]
    [InlineData("Battle.net\nUnable to connect")]
    [InlineData("Battle.net Please log in again")]
    public void IsShown_DetectsTheDisconnectDialog(string text)
    {
        Assert.True(BattleNetDisconnect.IsShown(text));
        Assert.True(BattleNetDisconnect.IsShown(text, loginInMemory: false));
        Assert.True(BattleNetDisconnect.IsShown(text, loginInMemory: null));
    }

    [Fact]
    public void IsShown_IgnoresTheLoadingScreen()
    {
        Assert.False(BattleNetDisconnect.IsShown("WELCOME TO THE NEXUS"));
    }

    [Theory]
    [InlineData(LoginFormText)]
    [InlineData("BATTLE.NET Email or Phone Password LOG IN Create a free Battle.net Account")]
    public void IsShown_NeverMatchesTheLoginForm(string text)
    {
        // #385: the login-form rule matches this text, so the disconnect rule must not.
        Assert.True(ClientScreenText.IsLoginForm(text));
        Assert.False(BattleNetDisconnect.IsShown(text));
        Assert.False(BattleNetDisconnect.IsShown(text, loginInMemory: null));
    }

    [Fact]
    public void IsShown_MemoryReadsTheLoginForm_IsNeverADisconnect()
    {
        // OCR missed the password field, so only "Battle.net" and "Log in" are left. Memory reads
        // ScreenLoginUnified with no dialog (Login): the login-form path decides.
        const string partial = "Log in Battle.net Account";

        Assert.False(ClientScreenText.IsLoginForm(partial));
        Assert.True(BattleNetDisconnect.IsShown(partial, loginInMemory: null));
        Assert.False(BattleNetDisconnect.IsShown(partial, loginInMemory: true));
        Assert.True(LoginFormCue.Sees(loginInMemory: true, partial));
    }
}
