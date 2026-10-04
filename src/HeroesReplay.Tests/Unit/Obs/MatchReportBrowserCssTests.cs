using System;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Obs;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchReportBrowserCssTests
{
    [Fact]
    public void Build_HidesTheSiteMenuAndEventBanner()
    {
        string css = MatchReportBrowserCss.Build(string.Empty, hideHeader: true);

        Assert.Contains("#main-menu", css);
        Assert.Contains(".main-navigation-wrapper", css);
        Assert.Contains(".alt-acct-nav", css);
        Assert.Contains("xalatath-scoreboard", css);
        Assert.Contains("horizontal-banner-ad", css);
        Assert.DoesNotContain("single-match", css);
    }

    [Fact]
    public void Build_WithTheConfiguredCss_HidesOnlyTheTopNavigationConsentAndAds()
    {
        string css = MatchReportBrowserCss.Build(AppSettingsReportCss(), hideHeader: true);

        Assert.Contains("#main-menu", css);
        Assert.Contains(".main-navigation-wrapper", css);
        Assert.Contains("#CybotCookiebotDialog", css);
        Assert.Contains("horizontal-banner-ad", css);
        Assert.DoesNotContain("/Match/Single/", css);
        Assert.DoesNotContain("replayID=", css);
        Assert.DoesNotContain(".footer-wrapper", css);
        Assert.Contains(".disclaimer", css);
        Assert.DoesNotContain("#hotsapi-alert", css);
        Assert.DoesNotContain(".flyout-menu", css);
        Assert.DoesNotContain("max-sm:text-sm", css);
    }

    [Fact]
    public void Build_IsTheSameWhateverOBSSavedBefore()
    {
        string first = MatchReportBrowserCss.Build("body{}", hideHeader: true);
        string second = MatchReportBrowserCss.Build("body{}", hideHeader: true);

        Assert.Equal("body{}\n" + MatchReportBrowserCss.Header, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Build_LeavesTheHeaderWhenHideIsOff_ButStillHidesEventOverlays()
    {
        string css = MatchReportBrowserCss.Build("nav{display:none}", hideHeader: false);

        Assert.Equal("nav{display:none}\n" + MatchReportBrowserCss.EventOverlays, css);
        Assert.DoesNotContain("#main-menu", css);
        Assert.Contains("xalatath-scoreboard", css);
    }

    /// <summary>
    /// #213: Build replaces the CSS OBS saved from the template, so every element the template
    /// hides must be hidden by Build too.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Build_HidesEverySelectorTheTemplateHides(bool hideHeader)
    {
        string built = MatchReportBrowserCss.Build(AppSettingsReportCss(), hideHeader);
        string[] navigation = { "#main-menu", ".main-navigation-wrapper", ".alt-acct-nav" };

        foreach (string selector in TemplateHiddenSelectors())
        {
            if (!hideHeader && Array.IndexOf(navigation, selector) >= 0)
            {
                continue;
            }

            Assert.Contains(selector, built);
        }
    }

    private static string[] TemplateHiddenSelectors()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "heroes-replay.slnx")))
        {
            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        }

        using JsonDocument template = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "obs", "Default.json"))
        );
        foreach (JsonElement source in template.RootElement.GetProperty("sources").EnumerateArray())
        {
            if (source.GetProperty("name").GetString() != "match-report-browser")
            {
                continue;
            }

            string css = source.GetProperty("settings").GetProperty("css").GetString();
            int rule = css.IndexOf("{display:none", StringComparison.Ordinal);
            int start = css.LastIndexOf('}', rule) + 1;
            return css.Substring(start, rule - start)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        throw new InvalidOperationException("obs/Default.json has no match-report-browser source.");
    }

    [Fact]
    public void WithScroll_MovesThePageToItsBottomOverTheDisplayTime()
    {
        string css = MatchReportBrowserCss.WithScroll("body{}", TimeSpan.FromSeconds(75));

        Assert.StartsWith("body{}\n" + MatchReportBrowserCss.ScrollStart, css);
        Assert.EndsWith(MatchReportBrowserCss.ScrollEnd, css);
        Assert.Contains("html{height:100vh!important;overflow:hidden!important;}", css);
        Assert.Contains("heroesreplay-report-scroll 69s linear 3s both!important", css);
        Assert.Contains("body{position:relative!important;", css);
        Assert.Contains("to{top:min(0px,calc(100vh - 10600px));}", css);
        // A transformed body stops being drawn about 8,000px down in OBS's browser source.
        Assert.DoesNotContain("transform", css, StringComparison.Ordinal);
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
    public void WithScroll_OnTheBuiltCssIsStable()
    {
        string report = MatchReportBrowserCss.WithScroll(
            MatchReportBrowserCss.Build("body{}", hideHeader: true),
            TimeSpan.FromSeconds(75)
        );

        string again = MatchReportBrowserCss.WithScroll(report, TimeSpan.FromSeconds(75));

        Assert.Equal(report, again);
    }

    private static string AppSettingsReportCss()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.GetProperty("OBS").GetProperty("ReportBrowserCss").GetString();
    }
}
