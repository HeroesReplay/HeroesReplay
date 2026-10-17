using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Clips;

/// <summary>Source-generated JSON for <c>clips.json</c> (#454): no reflection, so Native AOT writes it.</summary>
[JsonSerializable(typeof(List<ClipIndexRow>))]
internal sealed partial class ClipsJsonContext : JsonSerializerContext;

/// <summary>One cut clip in a replay's <c>clips.json</c>.</summary>
internal sealed record ClipIndexRow(
    string Kind,
    string Hero,
    string Description,
    int HudStart,
    int HudEnd,
    double FileStart,
    double Duration,
    string File,
    IReadOnlyList<ClipIndexKill> Kills
);

/// <summary>One killing blow of a <see cref="ClipIndexRow"/>.</summary>
internal sealed record ClipIndexKill(int Second, string Victim);
