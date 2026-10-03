using System;
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
        Assert.DoesNotContain("single-match", css);
    }

    [Fact]
    public void Apply_ShowsTheTeamSections()
    {
        string css = MatchReportBrowserCss.Apply(string.Empty, string.Empty, hideHeader: true);

        Assert.DoesNotContain("max-sm:text-sm", css);
        Assert.DoesNotContain("max-w-[1500px]", css);
    }

    [Fact]
    public void Apply_RemovesTheTeamSectionsRuleAnEarlierBuildSaved()
    {
        string fresh = MatchReportBrowserCss.Apply(string.Empty, "body{}", hideHeader: true);
        // The collection's own CSS ended with the rule, and the earlier build appended its
        // rules with the team sections rule before the header rule.
        string earlier = fresh.Replace(
            MatchReportBrowserCss.Header,
            MatchReportBrowserCss.RetiredTeamSections + MatchReportBrowserCss.Header
        );
        string saved = "html{} " + MatchReportBrowserCss.RetiredTeamSections + "\n" + earlier;

        string css = MatchReportBrowserCss.Apply(saved, "body{}", hideHeader: true);

        Assert.Equal("html{} \n" + fresh, css);
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
    public void WithScroll_MovesThePageToItsBottomOverTheDisplayTime()
    {
        string css = MatchReportBrowserCss.WithScroll("body{}", TimeSpan.FromSeconds(75));

        Assert.StartsWith("body{}\n" + MatchReportBrowserCss.ScrollStart, css);
        Assert.EndsWith(MatchReportBrowserCss.ScrollEnd, css);
        Assert.Contains("html{height:100vh!important;overflow:hidden!important;}", css);
        Assert.Contains("heroesreplay-report-scroll 69s linear 3s both!important", css);
        Assert.Contains("to{transform:translateY(min(0px,calc(100vh - 100%)));}", css);
    }

    [Fact]
    public void WithScroll_ReplacesTheBlockAnEarlierReportWrote()
    {
        string earlier = MatchReportBrowserCss.WithScroll("body{}", TimeSpan.FromSeconds(60));

        string css = MatchReportBrowserCss.WithScroll(earlier, TimeSpan.FromSeconds(75));

        Assert.Equal(MatchReportBrowserCss.WithScroll("body{}", TimeSpan.FromSeconds(75)), css);
        Assert.DoesNotContain("54s", css);
    }

    [Fact]
    public void WithScroll_ShortSceneScrollsForAllOfIt()
    {
        string css = MatchReportBrowserCss.WithScroll(string.Empty, TimeSpan.FromSeconds(5));

        Assert.StartsWith(MatchReportBrowserCss.ScrollStart, css);
        Assert.Contains("heroesreplay-report-scroll 5s linear 0s both!important", css);
    }

    [Fact]
    public void WithScroll_NoDisplayTimeLeavesNoAnimation()
    {
        string earlier = MatchReportBrowserCss.WithScroll("body{}", TimeSpan.FromSeconds(75));

        Assert.Equal("body{}", MatchReportBrowserCss.WithScroll(earlier, TimeSpan.Zero));
    }

    [Fact]
    public void Apply_KeepsTheScrollBlockForWithScrollToReplace()
    {
        string report = MatchReportBrowserCss.WithScroll(
            MatchReportBrowserCss.Apply(string.Empty, "body{}", hideHeader: true),
            TimeSpan.FromSeconds(75)
        );

        string again = MatchReportBrowserCss.WithScroll(
            MatchReportBrowserCss.Apply(report, "body{}", hideHeader: true),
            TimeSpan.FromSeconds(75)
        );

        Assert.Equal(report, again);
    }

    [Fact]
    public void Apply_DoesNotDuplicateTheSameRules()
    {
        string once = MatchReportBrowserCss.Apply(string.Empty, "body{}", hideHeader: true);
        string twice = MatchReportBrowserCss.Apply(once, "body{}", hideHeader: true);

        Assert.Equal(once, twice);
    }
}
