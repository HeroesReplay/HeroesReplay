using System;
using System.Globalization;

namespace HeroesReplay.Core.Obs;

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
    /// The rule earlier builds used to hide the Team Advanced HP MMR and Team Advanced Stats
    /// sections. Those team sections are shown now. OBS saved the rule into the browser
    /// source, so it is taken out of the current CSS rather than only left out of the new.
    /// </summary>
    public const string RetiredTeamSections =
        "div[class~=\"max-sm:text-sm\"][class~=\"max-w-[1500px]\"][class~=\"mx-auto\"][class~=\"my-5\"]"
        + "{display:none!important;}";

    private const string ReplayLink =
        " a[href*='/Match/Single/'],a[href*='replayID=']{font-size:0!important;color:transparent!important;pointer-events:none!important;}";

    /// <summary>
    /// The match-report browser source is one canvas tall (1920x1080). The page scrolls itself,
    /// so a page of any height reaches its bottom. OBS limits a browser source to 8192px, and
    /// the page with its team sections is about 10,000px.
    /// </summary>
    public const int SourceHeight = 1080;

    public const string ScrollStart = "/*heroesreplay-report-scroll*/";
    public const string ScrollEnd = "/*heroesreplay-report-scroll-end*/";

    /// <summary>The page rests this long at the top before it scrolls, and at the bottom after.</summary>
    public static readonly TimeSpan ScrollHold = TimeSpan.FromSeconds(3);

    public static string Apply(string current, string configured, bool hideHeader)
    {
        string extra = configured ?? string.Empty;
        extra += ReplayLink;
        if (hideHeader)
        {
            extra += Header;
        }

        string existing = (current ?? string.Empty).Replace(
            RetiredTeamSections,
            string.Empty,
            StringComparison.Ordinal
        );
        if (string.IsNullOrWhiteSpace(extra))
        {
            return existing;
        }

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

    /// <summary>
    /// The CSS with one scroll block: the body moves from the top of the page to its bottom
    /// over <paramref name="displayTime"/>, resting <see cref="ScrollHold"/> at each end. An
    /// earlier block is replaced, so a changed display time does not stack animations. The
    /// browser source restarts when its scene shows, which starts the animation again.
    /// </summary>
    public static string WithScroll(string css, TimeSpan displayTime)
    {
        string kept = WithoutScroll(css);
        if (displayTime <= TimeSpan.Zero)
        {
            return kept;
        }

        TimeSpan delay = ScrollHold;
        TimeSpan duration = displayTime - ScrollHold - ScrollHold;
        if (duration <= TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
            duration = displayTime;
        }

        string block =
            ScrollStart
            + "html{height:100vh!important;overflow:hidden!important;}"
            + "body{height:auto!important;min-height:0!important;overflow:visible!important;"
            + "animation:heroesreplay-report-scroll "
            + Seconds(duration)
            + " linear "
            + Seconds(delay)
            + " both!important;}"
            + "@keyframes heroesreplay-report-scroll{from{transform:translateY(0);}"
            + "to{transform:translateY(min(0px,calc(100vh - 100%)));}}"
            + ScrollEnd;
        return string.IsNullOrWhiteSpace(kept) ? block : kept + "\n" + block;
    }

    private static string WithoutScroll(string css)
    {
        string kept = css ?? string.Empty;
        int start = kept.IndexOf(ScrollStart, StringComparison.Ordinal);
        while (start >= 0)
        {
            int end = kept.IndexOf(ScrollEnd, start, StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }

            kept = (kept.Substring(0, start) + kept.Substring(end + ScrollEnd.Length)).TrimEnd();
            start = kept.IndexOf(ScrollStart, StringComparison.Ordinal);
        }

        return kept;
    }

    private static string Seconds(TimeSpan time) =>
        time.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "s";
}
