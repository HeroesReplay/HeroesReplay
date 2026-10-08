using System;
using System.Globalization;
using System.Linq;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

/// <summary>Small OBS scene collections in the shape OBS saves them, for the diff and plan tests.</summary>
internal static class ObsCollectionFixture
{
    public static string Document(params string[] sources) =>
        "{\"name\":\"HeroesReplay\",\"current_scene\":\"game-scene\",\"sources\":["
        + string.Join(",", sources)
        + "]}";

    /// <summary>
    /// A source with its settings and filters as JSON. Every call gets a new <c>uuid</c>, as OBS
    /// gives each source its own; it is never compared.
    /// </summary>
    public static string Source(
        string name,
        string kind = "browser_source",
        string settings = "{}",
        string[] filters = null
    ) =>
        "{\"prev_ver\":537001986,\"name\":\""
        + name
        + "\",\"uuid\":\""
        + Guid.NewGuid()
        + "\",\"id\":\""
        + kind
        + "\",\"versioned_id\":\""
        + kind
        + "\",\"settings\":"
        + settings
        + ",\"volume\":1.0,\"muted\":false,\"enabled\":true,\"hotkeys\":{\"x\":[]}"
        + (filters == null ? "" : ",\"filters\":[" + string.Join(",", filters) + "]")
        + "}";

    public static string Filter(
        string name,
        string kind = "crop_filter",
        string settings = "{}",
        bool enabled = true
    ) =>
        "{\"prev_ver\":537001986,\"name\":\""
        + name
        + "\",\"uuid\":\""
        + Guid.NewGuid()
        + "\",\"id\":\""
        + kind
        + "\",\"settings\":"
        + settings
        + ",\"enabled\":"
        + (enabled ? "true" : "false")
        + ",\"volume\":1.0}";

    public static string Scene(string name, params string[] items) =>
        Source(
            name,
            "scene",
            "{\"id_counter\":"
                + (items.Length + 7)
                + ",\"custom_size\":false,\"items\":["
                + string.Join(",", items)
                + "]}"
        );

    /// <summary>A scene item. OBS numbers items itself (<c>id</c>); that is never compared.</summary>
    public static string Item(
        string source,
        double x = 0,
        double y = 0,
        bool visible = true,
        int id = 1
    ) =>
        "{\"name\":\""
        + source
        + "\",\"source_uuid\":\""
        + Guid.NewGuid()
        + "\",\"visible\":"
        + (visible ? "true" : "false")
        + ",\"id\":"
        + id
        + ",\"pos\":{\"x\":"
        + x.ToString("0.0", CultureInfo.InvariantCulture)
        + ",\"y\":"
        + y.ToString("0.0", CultureInfo.InvariantCulture)
        + "},\"scale\":{\"x\":1.0,\"y\":1.0},\"private_settings\":{}}";

    /// <summary>The collection the tests start from: two report sources, a filter, and two scenes.</summary>
    public static string Layout(
        string css = "body{}",
        double rankX = 1799,
        bool cropEnabled = true,
        string cropTop = "0",
        string[] extraSources = null
    ) =>
        Document(
            new[]
            {
                Source(
                    "match-report-browser",
                    settings: "{\"url\":\"file:///C:/heroesreplay/Data/match-report.html\",\"css\":\""
                        + css
                        + "\",\"width\":1920}"
                ),
                Source(
                    "countdown",
                    settings: "{\"width\":1920}",
                    filters:
                    [
                        Filter(
                            "Crop/Pad",
                            settings: "{\"top\":" + cropTop + "}",
                            enabled: cropEnabled
                        ),
                    ]
                ),
                Source(
                    "rank-image",
                    "image_source",
                    "{\"file\":\"C:/heroesreplay/app/obs/images/rank.png\"}"
                ),
                Scene("game-scene", Item("rank-image", rankX, 0, id: 29), Item("countdown", id: 3)),
                Scene("match-report", Item("match-report-browser", id: 1)),
            }
                .Concat(extraSources ?? [])
                .ToArray()
        );
}
