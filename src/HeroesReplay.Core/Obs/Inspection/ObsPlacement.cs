using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs.Inspection;

/// <summary>
/// Where a scene item sits on the canvas: its position, the point of the item that position
/// anchors (<see cref="Alignment"/>, OBS's <c>OBS_ALIGN_*</c> bits), its scale, and its bounding
/// box. The same item read from <c>obs/Default.json</c> (<see cref="FromCollection"/>) and from
/// GetSceneItemTransform (<see cref="FromTransform"/>) can then be compared.
/// </summary>
public sealed record ObsPlacement(
    double X,
    double Y,
    int Alignment,
    double ScaleX,
    double ScaleY,
    string BoundsType,
    double BoundsWidth,
    double BoundsHeight
)
{
    /// <summary>A position or bounds this many canvas pixels away still counts as in place.</summary>
    public const double PixelTolerance = 2.0;

    /// <summary>A scale this far from the template's still counts as in place.</summary>
    public const double ScaleTolerance = 0.01;

    public const string NoBounds = "OBS_BOUNDS_NONE";

    /// <summary>OBS's <c>obs_bounds_type</c> values, in order, as obs-websocket names them.</summary>
    private static readonly string[] BoundsTypes =
    {
        NoBounds,
        "OBS_BOUNDS_STRETCH",
        "OBS_BOUNDS_SCALE_INNER",
        "OBS_BOUNDS_SCALE_OUTER",
        "OBS_BOUNDS_SCALE_TO_WIDTH",
        "OBS_BOUNDS_SCALE_TO_HEIGHT",
        "OBS_BOUNDS_MAX_ONLY",
    };

    /// <summary>A scene item as the collection file saves it (<c>pos</c>, <c>align</c>, <c>scale</c>, <c>bounds_type</c>, <c>bounds</c>).</summary>
    public static ObsPlacement FromCollection(JsonElement item)
    {
        int boundsType = (int)Number(item, "bounds_type");
        return new ObsPlacement(
            Vector(item, "pos", "x"),
            Vector(item, "pos", "y"),
            (int)Number(item, "align"),
            Vector(item, "scale", "x", 1),
            Vector(item, "scale", "y", 1),
            boundsType >= 0 && boundsType < BoundsTypes.Length
                ? BoundsTypes[boundsType]
                : boundsType.ToString(CultureInfo.InvariantCulture),
            Vector(item, "bounds", "x"),
            Vector(item, "bounds", "y")
        );
    }

    /// <summary>GetSceneItemTransform's <c>sceneItemTransform</c>.</summary>
    public static ObsPlacement FromTransform(JObject transform)
    {
        if (transform == null)
        {
            return null;
        }

        return new ObsPlacement(
            ObsResponse.Double(transform, "positionX") ?? 0,
            ObsResponse.Double(transform, "positionY") ?? 0,
            (int)(ObsResponse.Long(transform, "alignment") ?? 0),
            ObsResponse.Double(transform, "scaleX") ?? 1,
            ObsResponse.Double(transform, "scaleY") ?? 1,
            ObsResponse.String(transform, "boundsType") ?? NoBounds,
            ObsResponse.Double(transform, "boundsWidth") ?? 0,
            ObsResponse.Double(transform, "boundsHeight") ?? 0
        );
    }

    /// <summary>
    /// How this placement differs from <paramref name="expected"/>, one phrase each, or none
    /// when it is within <see cref="PixelTolerance"/> and <see cref="ScaleTolerance"/>. The
    /// scale is compared only without a bounding box: a bounding box sets the size itself.
    /// </summary>
    public IReadOnlyList<string> Differences(ObsPlacement expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var differences = new List<string>();
        if (Math.Abs(X - expected.X) > PixelTolerance || Math.Abs(Y - expected.Y) > PixelTolerance)
        {
            differences.Add("at " + Point(X, Y) + " instead of " + Point(expected.X, expected.Y));
        }

        if (Alignment != expected.Alignment)
        {
            differences.Add(
                "anchored "
                    + AlignmentName(Alignment)
                    + " instead of "
                    + AlignmentName(expected.Alignment)
            );
        }

        if (!string.Equals(BoundsType, expected.BoundsType, StringComparison.Ordinal))
        {
            differences.Add(
                "bounding box "
                    + BoundsName(BoundsType)
                    + " instead of "
                    + BoundsName(expected.BoundsType)
            );
        }
        else if (string.Equals(BoundsType, NoBounds, StringComparison.Ordinal))
        {
            if (
                Math.Abs(ScaleX - expected.ScaleX) > ScaleTolerance
                || Math.Abs(ScaleY - expected.ScaleY) > ScaleTolerance
            )
            {
                differences.Add(
                    "scaled "
                        + Size(ScaleX, ScaleY, "0.###")
                        + " instead of "
                        + Size(expected.ScaleX, expected.ScaleY, "0.###")
                );
            }
        }
        else if (
            Math.Abs(BoundsWidth - expected.BoundsWidth) > PixelTolerance
            || Math.Abs(BoundsHeight - expected.BoundsHeight) > PixelTolerance
        )
        {
            differences.Add(
                "bounded to "
                    + Size(BoundsWidth, BoundsHeight, "0.#")
                    + " instead of "
                    + Size(expected.BoundsWidth, expected.BoundsHeight, "0.#")
            );
        }

        return differences;
    }

    /// <summary>The anchor in words, with OBS's value: <c>bottom-left (9)</c>.</summary>
    public static string AlignmentName(int alignment)
    {
        // OBS_ALIGN_LEFT 1, RIGHT 2, TOP 4, BOTTOM 8; 0 is the centre.
        string vertical =
            (alignment & 4) != 0 ? "top"
            : (alignment & 8) != 0 ? "bottom"
            : null;
        string horizontal =
            (alignment & 1) != 0 ? "left"
            : (alignment & 2) != 0 ? "right"
            : null;
        string name =
            vertical == null && horizontal == null ? "center"
            : vertical == null ? horizontal
            : horizontal == null ? vertical
            : vertical + "-" + horizontal;
        return name + " (" + alignment.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private static string BoundsName(string type) =>
        type == null ? "none"
        : type.StartsWith("OBS_BOUNDS_", StringComparison.Ordinal)
            ? type.Substring("OBS_BOUNDS_".Length).ToLowerInvariant().Replace('_', ' ')
        : type;

    private static string Point(double x, double y) =>
        "("
        + x.ToString("0.#", CultureInfo.InvariantCulture)
        + ", "
        + y.ToString("0.#", CultureInfo.InvariantCulture)
        + ")";

    private static string Size(double width, double height, string format) =>
        width.ToString(format, CultureInfo.InvariantCulture)
        + "x"
        + height.ToString(format, CultureInfo.InvariantCulture);

    private static double Number(JsonElement item, string name, double fallback = 0) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : fallback;

    private static double Vector(JsonElement item, string name, string axis, double fallback = 0) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(name, out JsonElement vector)
        && vector.ValueKind == JsonValueKind.Object
            ? Number(vector, axis, fallback)
            : fallback;
}
