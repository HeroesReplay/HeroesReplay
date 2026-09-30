using System;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

/// <summary>
/// CSS for the Heroes Profile match page shown in the match-report browser source.
/// </summary>
public static class MatchReportBrowserCss
{
    /// <summary>
    /// Site menu, account bar, ads, and the event banner above <c>single-match</c>.
    /// Does not hide the match body.
    /// </summary>
    public const string Header =
        "#main-menu,.main-navigation-wrapper,.alt-acct-nav,#mobile-toggle,"
        + "horizontal-banner-ad,rich-media-ad,dynamic-banner-ad,takeover-ad,"
        + "xalatath-scoreboard,void-glitch,void-fallen-splash,void-stage-up,void-hidden-eye,void-whispers,mobile-nav-hack"
        + "{display:none!important;}";

    /// <summary>
    /// Team Advanced HP MMR tables. The same wrapper also holds Team Advanced Stats.
    /// Talent builds use a different width class and stay visible.
    /// </summary>
    public const string AdvancedMmr =
        "div[class~=\"max-sm:text-sm\"][class~=\"max-w-[1500px]\"][class~=\"mx-auto\"][class~=\"my-5\"]"
        + "{display:none!important;}";

    private const string ReplayLink =
        " a[href*='/Match/Single/'],a[href*='replayID=']{font-size:0!important;color:transparent!important;pointer-events:none!important;}";

    public static string Apply(string current, string configured, bool hideHeader)
    {
        string extra = configured ?? string.Empty;
        extra += ReplayLink;
        extra += AdvancedMmr;
        if (hideHeader)
        {
            extra += Header;
        }

        if (string.IsNullOrWhiteSpace(extra))
        {
            return current ?? string.Empty;
        }

        string existing = current ?? string.Empty;
        if (existing.Contains(extra, StringComparison.Ordinal))
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            return extra;
        }

        return existing + "\n" + extra;
    }
}
