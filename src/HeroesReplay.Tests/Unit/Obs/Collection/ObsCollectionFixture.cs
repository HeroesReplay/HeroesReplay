using System;
using System.Globalization;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using Microsoft.Extensions.Configuration;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

/// <summary>Small OBS scene collections in the shape OBS saves them, for the diff and plan tests.</summary>
internal static class ObsCollectionFixture
{
    /// <summary>
    /// <c>obs/Default.json</c> as it was at SHA-256 <c>576DBCEA…4309</c> (#367), the template
    /// <see cref="Obs32Saved"/> was written from.
    /// </summary>
    public static string Obs32Template => ObsAsset("obs-32.2.2-template.json");

    /// <summary>
    /// <see cref="Obs32Template"/> after OBS 32.2.2 loaded and saved it, with no edits: ASA-SERVER's
    /// <c>HeroesReplay.json</c> on 2026-10-08 (#367). OBS added <c>pos_rel</c>, <c>scale_rel</c>,
    /// <c>bounds_rel</c>, and <c>scale_ref</c> to all 26 scene items, and saved the
    /// <c>waiting-screen</c> game capture at (139, 330), 395.5x226 for the template's
    /// (139.01, 329.99), 395.38x226. The spectator's values (the replay text, the report urls and
    /// css, the rank images it showed) are as it left them. Two things were set back to the
    /// template: the asset paths, which HeroesReplay had pointed at its stable copy
    /// (<c>...\obs\assets\7BA1951F14EE134C\</c>, #330), and <c>sc2-main-menu-alarak</c>'s
    /// <c>monitoring_type</c>, 2 on that box, the one value someone changed there.
    /// </summary>
    public static string Obs32Saved => ObsAsset("obs-32.2.2-saved.json");

    /// <summary>The values the packaged settings let the spectator set (<c>appsettings.json</c> and the dev overlay).</summary>
    public static ObsRuntimeValues PackagedRuntime() =>
        ObsRuntimeValues.From(
            new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.dev.json", optional: true)
                .Build()
                .GetSection("OBS")
                .Get<OBSSettings>()
        );

    private static string ObsAsset(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Obs", name));

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
