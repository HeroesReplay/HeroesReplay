using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

/// <summary>
/// The countdown on <c>waiting-screen</c> (<c>obs/countdown/index.html</c>). When the clock
/// reaches zero it shows the end text until the next replay loads, which can take minutes. That
/// text was set at the clock's 22vw size and ran past both edges of the source (issue #248).
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class CountdownPageTests
{
    // Bahnschrift SemiBold capitals are about 0.62em wide; a little over is the safe side.
    private const double CapitalWidthEm = 0.66;

    [Fact]
    public void EndText_FitsTheTemplatesCountdownSource()
    {
        (string end, string title, int width) = TemplateCountdown();
        string page = Page();

        string done = Rule(page, "#clock.done");
        (double minPx, double vw, double maxPx) = Clamp(done);
        double fontPx = Math.Clamp(vw * width / 100, minPx, maxPx);
        double spacingEm = Em(done, "letter-spacing");
        double textPx = end.Length * (CapitalWidthEm + spacingEm) * fontPx;

        Assert.Equal("STARTING", end);
        Assert.Equal("NEXT MATCH", title);
        Assert.True(
            textPx <= width * 0.94,
            $"'{end}' is about {textPx:0}px at {fontPx:0}px in a {width}px source."
        );
        Assert.True(maxPx <= 96, $"The end text may grow to {maxPx}px.");
    }

    [Fact]
    public void LongTitleOrEndText_ShrinksToThePageWidth()
    {
        string page = Page();

        Assert.Contains("white-space: nowrap", Rule(page, "#title,\n      #clock"));
        Assert.Contains("function fit(el)", page);
        Assert.Contains("fit(titleEl);", page);
        Assert.Contains("fit(clockEl);", page);
        Assert.Contains("window.addEventListener(\"resize\"", page);
    }

    private static (string End, string Title, int Width) TemplateCountdown()
    {
        using JsonDocument collection = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoRoot(), "obs", "Default.json"))
        );
        JsonElement settings = collection
            .RootElement.GetProperty("sources")
            .EnumerateArray()
            .Single(source => source.GetProperty("name").GetString() == "countdown")
            .GetProperty("settings");
        string url = settings.GetProperty("url").GetString();
        string query = url.Substring(url.IndexOf('?') + 1);
        string end = null;
        string title = null;
        foreach (string pair in query.Split('&'))
        {
            string[] parts = pair.Split('=', 2);
            string value = Uri.UnescapeDataString(parts[1]);
            if (parts[0] == "end")
            {
                end = value;
            }
            else if (parts[0] == "title")
            {
                title = value;
            }
        }

        return (end, title, settings.GetProperty("width").GetInt32());
    }

    private static string Page() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "obs", "countdown", "index.html"))
            .Replace("\r\n", "\n");

    private static string Rule(string page, string selector)
    {
        int start = page.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No CSS rule for {selector}.");
        int end = page.IndexOf('}', start);
        return page.Substring(start, end - start);
    }

    private static (double MinPx, double Vw, double MaxPx) Clamp(string rule)
    {
        Match match = Regex.Match(
            rule,
            @"font-size:\s*clamp\(\s*([\d.]+)px,\s*([\d.]+)vw,\s*([\d.]+)px\s*\)"
        );
        Assert.True(match.Success, "The end text needs its own clamp() font-size.");
        return (Number(match.Groups[1]), Number(match.Groups[2]), Number(match.Groups[3]));
    }

    private static double Em(string rule, string property)
    {
        Match match = Regex.Match(rule, property + @":\s*([\d.]+)em");
        Assert.True(match.Success, $"No {property} in em.");
        return Number(match.Groups[1]);
    }

    private static double Number(Group group) =>
        double.Parse(group.Value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "heroes-replay.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("heroes-replay.slnx was not found.");
    }
}
