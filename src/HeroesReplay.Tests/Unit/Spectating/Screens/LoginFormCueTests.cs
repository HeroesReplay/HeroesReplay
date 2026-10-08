using HeroesReplay.Core.Spectating.Screens;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Screens;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class LoginFormCueTests
{
    private const string FormText =
        "Email or Phone Password Keep me logged in Log in Battle.net Account";

    [Fact]
    public void Sees_MemoryDecidesWhenItCanTell()
    {
        // Memory reads the form while OCR read an older frame, and memory reads Battle.net's
        // AUTHENTICATION panel (not the form) while OCR still shows the form's words.
        Assert.True(LoginFormCue.Sees(loginInMemory: true, "AUTHENTICATION Connecting..."));
        Assert.False(LoginFormCue.Sees(loginInMemory: false, FormText));
    }

    [Fact]
    public void Sees_OcrOnlyWhenMemoryCannotTell()
    {
        Assert.True(LoginFormCue.Sees(loginInMemory: null, FormText));
        Assert.False(LoginFormCue.Sees(loginInMemory: null, "PLAY COLLECTION LOOT WATCH"));
        Assert.False(LoginFormCue.Sees(loginInMemory: null, null));
    }
}
