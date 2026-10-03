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
    /// Heroes Profile's event overlays and its mobile menu. They are never part of the report,
    /// so they stay hidden when <c>OBS:HideReportHeader</c> is off (#213).
    /// </summary>
    public const string EventOverlays =
        "#mobile-toggle,xalatath-scoreboard,void-glitch,void-fallen-splash,void-stage-up,"
        + "void-hidden-eye,void-whispers,mobile-nav-hack"
        + "{display:none!important;}";

    /// <summary>
    /// The match-report browser source is one canvas tall (1920x1080). The page scrolls itself,
    /// so a page of any height reaches its bottom. OBS limits a browser source to 8192px, and
    /// the page with its team sections is about 10,000px.
    /// </summary>
    public const int SourceHeight = 1080;

    /// <summary>
    /// How far the page moves: the Heroes Profile match page with both team sections is about
    /// 10,350 to 10,650px tall at 1920px wide. The scroll animates <c>top</c>, not
    /// <c>transform</c>, so it needs a length: OBS's browser source (CEF without GPU) stops
    /// drawing a transformed body once it is about 8,000px down, and the frame freezes or goes
    /// blank. A <c>top</c> offset is laid out and painted like a scrolled page.
    /// </summary>
    public const int PageHeight = 10600;

    public const string ScrollStart = "/*heroesreplay-report-scroll*/";
    public const string ScrollEnd = "/*heroesreplay-report-scroll-end*/";

    /// <summary>The page rests this long at the top before it scrolls, and at the bottom after.</summary>
    public static readonly TimeSpan ScrollHold = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The whole CSS of a report browser source: <c>OBS:ReportBrowserCss</c>, then the
    /// <see cref="Header"/> rule, or only <see cref="EventOverlays"/> when the header stays.
    /// It replaces what OBS saved in the source, so rules that earlier builds wrote there
    /// (hidden replay links, footer, team sections) do not pile up.
    /// </summary>
    public static string Build(string configured, bool hideHeader)
    {
        string css = configured?.Trim() ?? string.Empty;
        string hidden = hideHeader ? Header : EventOverlays;
        return css.Length == 0 ? hidden : css + "\n" + hidden;
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
            + "body{position:relative!important;height:auto!important;min-height:0!important;"
            + "overflow:visible!important;animation:heroesreplay-report-scroll "
            + Seconds(duration)
            + " linear "
            + Seconds(delay)
            + " both!important;}"
            + "@keyframes heroesreplay-report-scroll{from{top:0;}"
            + "to{top:min(0px,calc(100vh - "
            + PageHeight.ToString(CultureInfo.InvariantCulture)
            + "px));}}"
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

            kept = (kept.Substring(0, start) + kept.Substring(end + ScrollEnd.Length)).Trim();
            start = kept.IndexOf(ScrollStart, StringComparison.Ordinal);
        }

        return kept;
    }

    private static string Seconds(TimeSpan time) =>
        time.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "s";
}
