using HeroesReplay.Core.Obs;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchReportBrowserCssTests
{
    [Fact]
    public void Apply_HidesTheSiteMenuAndEventBanner()
    {
        string css = MatchReportBrowserCss.Apply(string.Empty, string.Empty, hideHeader: true);

        Assert.Contains("#main-menu", css);
        Assert.Contains(".main-navigation-wrapper", css);
        Assert.Contains(".alt-acct-nav", css);
        Assert.Contains("xalatath-scoreboard", css);
        Assert.Contains("horizontal-banner-ad", css);
        Assert.Contains("max-sm:text-sm", css);
        Assert.Contains("max-w-[1500px]", css);
        Assert.Contains("my-5", css);
        Assert.DoesNotContain("single-match", css);
    }

    [Fact]
    public void Apply_LeavesTheHeaderWhenHideIsOff()
    {
        string css = MatchReportBrowserCss.Apply("body{}", "nav{display:none}", hideHeader: false);

        Assert.DoesNotContain("#main-menu", css);
        Assert.DoesNotContain("xalatath-scoreboard", css);
        Assert.Contains("nav{display:none}", css);
        Assert.StartsWith("body{}", css);
    }

    [Fact]
    public void Apply_DoesNotDuplicateTheSameRules()
    {
        string once = MatchReportBrowserCss.Apply(string.Empty, "body{}", hideHeader: true);
        string twice = MatchReportBrowserCss.Apply(once, "body{}", hideHeader: true);

        Assert.Equal(once, twice);
    }
}
