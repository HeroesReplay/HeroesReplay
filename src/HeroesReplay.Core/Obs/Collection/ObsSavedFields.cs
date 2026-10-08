using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary>A field OBS writes into a scene collection by itself when it saves one.</summary>
/// <param name="Name">The key, as OBS saves it.</param>
/// <param name="Since">The first OBS version that writes it.</param>
/// <param name="Why">What it is, and why it is not the operator's work.</param>
public sealed record ObsSavedField(string Name, string Since, string Why);

/// <summary>
/// The fields OBS writes into a scene collection on its own when it saves one, by the OBS version
/// that started writing them (#367). <c>obs/Default.json</c> does not carry them, so a collection
/// that OBS has only loaded and saved would otherwise show them as the operator's additions.
/// <see cref="ObsCollectionDiff"/> does not compare them, and when a merge takes a template change
/// to an item's position, scale, or bounding box it removes them from that item, so OBS loads the
/// template's values instead of the relative copies of the old ones.
/// </summary>
public static class ObsSavedFields
{
    /// <summary>
    /// Scene item fields. OBS 31.0 keeps each item's transform relative to the canvas, so a
    /// canvas resize does not move it, and saves that next to the absolute one. On load it takes
    /// <c>pos_rel</c> and <c>scale_rel</c> over <c>pos</c> and <c>scale</c> when both of those and
    /// <c>scale_ref</c> are there, and <c>bounds_rel</c> over <c>bounds</c> (libobs
    /// <c>obs-scene.c</c>, <c>scene_load_item</c>). OBS 32.2.2 saved all four on every one of the
    /// 26 items of <c>obs/Default.json</c> (#367): 208 lines that <c>obs plan</c> called operator
    /// additions. OBS 30.2 does not write them.
    /// </summary>
    public static readonly IReadOnlyList<ObsSavedField> SceneItem =
    [
        new(
            "pos_rel",
            "31.0",
            "The position relative to the canvas: canvas-height-halves from its centre."
        ),
        new("scale_rel", "31.0", "The scale relative to scale_ref."),
        new("bounds_rel", "31.0", "The bounding box in canvas-height-halves."),
        new(
            "scale_ref",
            "31.0",
            "The canvas size the relative transform was saved against (1920x1080)."
        ),
    ];

    /// <summary>The scene item fields <see cref="SceneItem"/> lists, by name.</summary>
    public static readonly IReadOnlySet<string> SceneItemNames = SceneItem
        .Select(field => field.Name)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The item properties those relative copies are made from: a merge that changes one of them
    /// drops the copies, or OBS would load the old transform from them.
    /// </summary>
    public static bool IsTransform(string property) =>
        property != null
        && (
            property is "pos" or "scale" or "bounds"
            || property.StartsWith("pos.", StringComparison.Ordinal)
            || property.StartsWith("scale.", StringComparison.Ordinal)
            || property.StartsWith("bounds.", StringComparison.Ordinal)
        );
}
