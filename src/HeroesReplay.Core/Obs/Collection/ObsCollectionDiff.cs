using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary>What one difference between the live collection and the template is.</summary>
public enum ObsDiffKind
{
    /// <summary>The template changed the value and the live one is still the old template's.</summary>
    ManagedChange,

    /// <summary>The template added it, and the live collection does not have it yet.</summary>
    ManagedAddition,

    /// <summary>The template removed it, and the live one is still the old template's.</summary>
    ManagedRemoval,

    /// <summary>The template did not change it, and the live value differs: the operator's.</summary>
    OperatorOverride,

    /// <summary>Only the live collection has it.</summary>
    OperatorAddition,

    /// <summary>The template has it, unchanged, and the live collection does not: never re-added.</summary>
    OperatorRemoval,

    /// <summary>The template and the operator both changed it.</summary>
    Conflict,

    /// <summary>
    /// It differs, but the template the live collection was written from is not known, so
    /// whether the template or the operator changed it cannot be told.
    /// </summary>
    Unattributed,
}

/// <summary>What a difference belongs to.</summary>
public enum ObsDiffEntity
{
    /// <summary>A source or a scene (OBS stores scenes as sources) or a group.</summary>
    Source,

    /// <summary>A filter on a source.</summary>
    Filter,

    /// <summary>A source placed in a scene: its transform, crop, and visibility.</summary>
    SceneItem,
}

/// <summary>What a merge that keeps the operator's work would do with a difference.</summary>
public enum ObsDiffApply
{
    /// <summary>Take the template's value (or add or remove it as the template does).</summary>
    Template,

    /// <summary>Keep the live value: the operator's, or one that cannot be attributed.</summary>
    Keep,

    /// <summary>Stop: both sides changed it.</summary>
    Refuse,
}

/// <summary>
/// One difference. Values are canonical JSON (object keys sorted, numbers in round-trip form);
/// null means the property or the whole entity is absent there, or the base is not known.
/// </summary>
public sealed record ObsCollectionDifference
{
    public ObsDiffKind Kind { get; init; }

    public ObsDiffEntity Entity { get; init; }

    /// <summary>The source or scene; for a scene item, the source the item shows.</summary>
    public string Source { get; init; }

    /// <summary>For a scene item, the scene it is in.</summary>
    public string Scene { get; init; }

    /// <summary>For a filter, its name.</summary>
    public string Filter { get; init; }

    /// <summary>The property, dotted (<c>settings.url</c>, <c>pos.x</c>). Null when the whole entity differs.</summary>
    public string Property { get; init; }

    public string Base { get; init; }

    public string Template { get; init; }

    public string Live { get; init; }

    public ObsDiffApply Apply { get; init; }

    /// <summary>One line, for text output.</summary>
    public string Describe()
    {
        string where = Entity switch
        {
            ObsDiffEntity.Filter => $"filter '{Filter}' on '{Source}'",
            ObsDiffEntity.SceneItem => $"'{Source}' in scene '{Scene}'",
            _ => $"source '{Source}'",
        };
        string what = Property == null ? where : $"{where} {Property}";
        string values =
            Property == null
                ? string.Empty
                : $": template {Template ?? "(none)"}, live {Live ?? "(none)"}"
                    + (Base == null ? string.Empty : $", was {Base}");
        return what + values;
    }
}

/// <param name="BaseKnown">The template the live collection was last written from was available.</param>
/// <param name="Differences">Every difference, sources first, then filters, then scene items.</param>
public sealed record ObsCollectionDiffResult(
    bool BaseKnown,
    IReadOnlyList<ObsCollectionDifference> Differences
)
{
    public int Count(ObsDiffKind kind) => Differences.Count(difference => difference.Kind == kind);

    public bool HasConflicts =>
        Differences.Any(difference => difference.Kind == ObsDiffKind.Conflict);
}

/// <summary>
/// A structured, three-way diff of scene collections (#307): the template the live collection was
/// last written from (the base), this install's template, and the live file, each after the path
/// rewrite, compared property by property for every source, filter, and scene item. The base
/// tells who changed a value: the template (a managed change), the operator (an override,
/// addition, or removal), or both (a conflict). Without a base, a difference is unattributed.
/// Ids OBS assigns (<c>uuid</c>, scene item ids), hotkeys, private settings, and the plug-in
/// version stamps are not compared, nor are the order of sources, filters, and items, global
/// audio, and transitions.
/// </summary>
public static class ObsCollectionDiff
{
    private static readonly JsonSerializerOptions Strings = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Not compared on a source or scene.</summary>
    private static readonly HashSet<string> SourceIgnored = new(StringComparer.Ordinal)
    {
        "name",
        "id",
        "uuid",
        "versioned_id",
        "prev_ver",
        "hotkeys",
        "private_settings",
        "canvas_uuid",
        "filters",
        "settings",
    };

    /// <summary>Only these are compared on a filter: its kind, whether it is on, and its settings.</summary>
    private static readonly HashSet<string> FilterCompared = new(StringComparer.Ordinal)
    {
        "enabled",
    };

    private static readonly HashSet<string> ItemIgnored = new(StringComparer.Ordinal)
    {
        "name",
        "source_uuid",
        "id",
        "private_settings",
        "group_item_backup",
    };

    /// <summary>Scene and group settings that OBS keeps as bookkeeping or that are compared per item.</summary>
    private static readonly HashSet<string> SceneSettingsIgnored = new(StringComparer.Ordinal)
    {
        "items",
        "id_counter",
    };

    /// <param name="baseJson">The template the live collection was last written from, or null when not known.</param>
    /// <param name="templateJson">The template an update would install.</param>
    /// <param name="liveJson">The live collection.</param>
    /// <param name="runtime">Values HeroesReplay sets while it runs; they are not compared.</param>
    /// <exception cref="JsonException">One of the documents is not valid JSON.</exception>
    public static ObsCollectionDiffResult Compare(
        string baseJson,
        string templateJson,
        string liveJson,
        ObsRuntimeValues runtime = null
    )
    {
        runtime ??= ObsRuntimeValues.None;
        Shape template = Shape.Parse(templateJson, runtime);
        Shape live = Shape.Parse(liveJson, runtime);
        Shape based = string.IsNullOrWhiteSpace(baseJson) ? null : Shape.Parse(baseJson, runtime);
        var differences = new List<ObsCollectionDifference>();
        var hidden = new HashSet<string>(StringComparer.Ordinal);

        // Sources first: one whose whole entity differs hides its filters and scene items.
        foreach (EntityKey key in Keys(based, template, live, ObsDiffEntity.Source))
        {
            if (CompareEntity(key, based, template, live, differences))
            {
                hidden.Add(key.Name);
            }
        }

        foreach (
            EntityKey key in Keys(based, template, live, ObsDiffEntity.Filter)
                .Where(key => !hidden.Contains(key.Owner))
        )
        {
            CompareEntity(key, based, template, live, differences);
        }

        foreach (
            EntityKey key in Keys(based, template, live, ObsDiffEntity.SceneItem)
                .Where(key => !hidden.Contains(key.Owner) && !hidden.Contains(key.SourceName))
        )
        {
            CompareEntity(key, based, template, live, differences);
        }

        return new ObsCollectionDiffResult(based != null, differences);
    }

    /// <summary>True when the entity differs as a whole (present on one side only).</summary>
    private static bool CompareEntity(
        EntityKey key,
        Shape based,
        Shape template,
        Shape live,
        List<ObsCollectionDifference> differences
    )
    {
        IReadOnlyDictionary<string, string> b = based?.Get(key);
        IReadOnlyDictionary<string, string> n = template.Get(key);
        IReadOnlyDictionary<string, string> l = live.Get(key);
        if (l != null && n != null)
        {
            foreach (
                string property in n
                    .Keys.Concat(l.Keys)
                    .Concat(b?.Keys ?? [])
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
            )
            {
                string templateValue = Value(n, property);
                string liveValue = Value(l, property);
                if (string.Equals(templateValue, liveValue, StringComparison.Ordinal))
                {
                    continue;
                }

                string baseValue = based == null ? null : Value(b, property);
                (ObsDiffKind kind, ObsDiffApply apply) = Classify(
                    based != null,
                    baseValue,
                    templateValue,
                    liveValue
                );
                differences.Add(
                    Difference(key, property, kind, apply, baseValue, templateValue, liveValue)
                );
            }

            return false;
        }

        if (l == null && n == null)
        {
            // Removed by the template and by the operator: nothing differs.
            return false;
        }

        ObsDiffKind entityKind;
        ObsDiffApply entityApply;
        if (based == null)
        {
            (entityKind, entityApply) = (ObsDiffKind.Unattributed, ObsDiffApply.Keep);
        }
        else if (l != null)
        {
            // Only the live collection has it now.
            (entityKind, entityApply) =
                b == null ? (ObsDiffKind.OperatorAddition, ObsDiffApply.Keep)
                : Same(b, l) ? (ObsDiffKind.ManagedRemoval, ObsDiffApply.Template)
                : (ObsDiffKind.Conflict, ObsDiffApply.Refuse);
        }
        else
        {
            // Only the template has it: added by the template, or removed by the operator.
            (entityKind, entityApply) =
                b == null
                    ? (ObsDiffKind.ManagedAddition, ObsDiffApply.Template)
                    : (ObsDiffKind.OperatorRemoval, ObsDiffApply.Keep);
        }

        differences.Add(Difference(key, null, entityKind, entityApply, null, null, null));
        return true;
    }

    private static (ObsDiffKind, ObsDiffApply) Classify(
        bool baseKnown,
        string based,
        string template,
        string live
    )
    {
        if (!baseKnown)
        {
            return (ObsDiffKind.Unattributed, ObsDiffApply.Keep);
        }

        if (string.Equals(based, template, StringComparison.Ordinal))
        {
            // The template did not change it: the live value is the operator's.
            return live == null ? (ObsDiffKind.OperatorRemoval, ObsDiffApply.Keep)
                : based == null ? (ObsDiffKind.OperatorAddition, ObsDiffApply.Keep)
                : (ObsDiffKind.OperatorOverride, ObsDiffApply.Keep);
        }

        if (string.Equals(based, live, StringComparison.Ordinal))
        {
            // Only the template moved.
            return template == null ? (ObsDiffKind.ManagedRemoval, ObsDiffApply.Template)
                : based == null ? (ObsDiffKind.ManagedAddition, ObsDiffApply.Template)
                : (ObsDiffKind.ManagedChange, ObsDiffApply.Template);
        }

        return (ObsDiffKind.Conflict, ObsDiffApply.Refuse);
    }

    private static ObsCollectionDifference Difference(
        EntityKey key,
        string property,
        ObsDiffKind kind,
        ObsDiffApply apply,
        string based,
        string template,
        string live
    ) =>
        new()
        {
            Kind = kind,
            Entity = key.Entity,
            Source = key.SourceName,
            Scene = key.Entity == ObsDiffEntity.SceneItem ? key.Owner : null,
            Filter = key.Entity == ObsDiffEntity.Filter ? key.Name : null,
            Property = property,
            Base = based,
            Template = template,
            Live = live,
            Apply = apply,
        };

    private static bool Same(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right
    ) =>
        left.Count == right.Count
        && left.All(pair =>
            right.TryGetValue(pair.Key, out string value)
            && string.Equals(pair.Value, value, StringComparison.Ordinal)
        );

    private static string Value(IReadOnlyDictionary<string, string> values, string property) =>
        values != null && values.TryGetValue(property, out string value) ? value : null;

    private static IEnumerable<EntityKey> Keys(
        Shape based,
        Shape template,
        Shape live,
        ObsDiffEntity entity
    ) =>
        (based?.Keys ?? [])
            .Concat(template.Keys)
            .Concat(live.Keys)
            .Where(key => key.Entity == entity)
            .Distinct()
            .OrderBy(key => key.Owner ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(key => key.Name, StringComparer.Ordinal);

    /// <summary>
    /// A source, filter, or scene item. <see cref="Owner"/> is the source of a filter or the scene
    /// of an item; <see cref="Name"/> is the source, the filter, or the item's source (with
    /// <c>#2</c>, <c>#3</c> for a source placed more than once in one scene).
    /// </summary>
    private readonly record struct EntityKey(ObsDiffEntity Entity, string Owner, string Name)
    {
        public string SourceName =>
            Entity switch
            {
                ObsDiffEntity.Filter => Owner,
                ObsDiffEntity.SceneItem => Name.Split('#')[0],
                _ => Name,
            };
    }

    /// <summary>Every entity of one collection, flattened to property → canonical value.</summary>
    private sealed class Shape
    {
        private readonly Dictionary<EntityKey, Dictionary<string, string>> entities = [];
        private readonly ObsRuntimeValues runtime;

        private Shape(ObsRuntimeValues runtime)
        {
            this.runtime = runtime;
        }

        public IEnumerable<EntityKey> Keys => entities.Keys;

        public IReadOnlyDictionary<string, string> Get(EntityKey key) =>
            entities.TryGetValue(key, out Dictionary<string, string> values) ? values : null;

        public static Shape Parse(string json, ObsRuntimeValues runtime)
        {
            var shape = new Shape(runtime);
            using JsonDocument document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(json) ? "{}" : json
            );
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return shape;
            }

            foreach (string list in new[] { "sources", "groups" })
            {
                if (
                    document.RootElement.TryGetProperty(list, out JsonElement sources)
                    && sources.ValueKind == JsonValueKind.Array
                )
                {
                    foreach (JsonElement source in sources.EnumerateArray())
                    {
                        shape.AddSource(source);
                    }
                }
            }

            return shape;
        }

        private void AddSource(JsonElement source)
        {
            string name = NameOf(source);
            if (name == null)
            {
                return;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            string kind = StringOf(source, "id");
            if (kind != null)
            {
                values["kind"] = kind;
            }

            bool scene = kind is "scene" or "group";
            foreach (JsonProperty property in source.EnumerateObject())
            {
                if (!SourceIgnored.Contains(property.Name))
                {
                    Flatten(property.Value, property.Name, values);
                }
            }

            if (
                source.TryGetProperty("settings", out JsonElement settings)
                && settings.ValueKind == JsonValueKind.Object
            )
            {
                foreach (JsonProperty property in settings.EnumerateObject())
                {
                    if (!scene || !SceneSettingsIgnored.Contains(property.Name))
                    {
                        Flatten(property.Value, "settings." + property.Name, values);
                    }
                }

                if (
                    scene
                    && settings.TryGetProperty("items", out JsonElement items)
                    && items.ValueKind == JsonValueKind.Array
                )
                {
                    AddItems(name, items);
                }
            }

            values = values
                .Where(pair => !runtime.SetsSource(name, pair.Key))
                .ToDictionary(StringComparer.Ordinal);
            entities.TryAdd(new EntityKey(ObsDiffEntity.Source, null, name), values);
            if (
                source.TryGetProperty("filters", out JsonElement filters)
                && filters.ValueKind == JsonValueKind.Array
            )
            {
                foreach (JsonElement filter in filters.EnumerateArray())
                {
                    AddFilter(name, filter);
                }
            }
        }

        private void AddFilter(string source, JsonElement filter)
        {
            string name = NameOf(filter);
            if (name == null)
            {
                return;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            string kind = StringOf(filter, "id");
            if (kind != null)
            {
                values["kind"] = kind;
            }

            foreach (JsonProperty property in filter.EnumerateObject())
            {
                if (FilterCompared.Contains(property.Name))
                {
                    Flatten(property.Value, property.Name, values);
                }
                else if (property.Name == "settings")
                {
                    Flatten(property.Value, "settings", values);
                }
            }

            entities.TryAdd(new EntityKey(ObsDiffEntity.Filter, source, name), values);
        }

        private void AddItems(string scene, JsonElement items)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (JsonElement item in items.EnumerateArray())
            {
                string source = NameOf(item);
                if (source == null)
                {
                    continue;
                }

                int count = seen.TryGetValue(source, out int earlier) ? earlier + 1 : 1;
                seen[source] = count;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (JsonProperty property in item.EnumerateObject())
                {
                    if (
                        !ItemIgnored.Contains(property.Name)
                        && !runtime.SetsItem(scene, source, property.Name)
                    )
                    {
                        Flatten(property.Value, property.Name, values);
                    }
                }

                entities.TryAdd(
                    new EntityKey(
                        ObsDiffEntity.SceneItem,
                        scene,
                        count == 1 ? source : source + "#" + count
                    ),
                    values
                );
            }
        }

        private static string NameOf(JsonElement element) =>
            element.ValueKind == JsonValueKind.Object ? StringOf(element, "name") : null;

        private static string StringOf(JsonElement element, string property) =>
            element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        /// <summary>Objects become dotted paths; anything else is one canonical value.</summary>
        private static void Flatten(
            JsonElement element,
            string path,
            Dictionary<string, string> into
        )
        {
            if (element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Any())
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    Flatten(property.Value, path + "." + property.Name, into);
                }

                return;
            }

            into[path] = Canonical(element);
        }
    }

    /// <summary>
    /// The value as JSON with sorted object keys and canonical numbers, so <c>1.0</c> and
    /// <c>1</c>, or reordered keys, are the same value.
    /// </summary>
    private static string Canonical(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => "{"
                + string.Join(
                    ",",
                    element
                        .EnumerateObject()
                        .OrderBy(property => property.Name, StringComparer.Ordinal)
                        .Select(property =>
                            JsonSerializer.Serialize(property.Name, Strings)
                            + ":"
                            + Canonical(property.Value)
                        )
                )
                + "}",
            JsonValueKind.Array => "["
                + string.Join(",", element.EnumerateArray().Select(Canonical))
                + "]",
            JsonValueKind.String => JsonSerializer.Serialize(element.GetString(), Strings),
            JsonValueKind.Number => Number(element.GetRawText()),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "null",
        };

    /// <summary>
    /// A whole number stays exact (colors are 32-bit integers). A fraction is compared at the
    /// single precision OBS keeps: it saves <c>0.66</c> back as <c>0.6600000262260437</c>.
    /// </summary>
    private static string Number(string raw)
    {
        if (
            raw.IndexOfAny(['.', 'e', 'E']) < 0
            && long.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long whole
            )
        )
        {
            return whole.ToString(CultureInfo.InvariantCulture);
        }

        if (
            !double.TryParse(
                raw,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double number
            )
        )
        {
            return raw;
        }

        return number == Math.Floor(number) && Math.Abs(number) < 1e15
            ? ((long)number).ToString(CultureInfo.InvariantCulture)
            : ((float)number).ToString("R", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// The collection values HeroesReplay itself sets while it runs (<see cref="ObsController"/>), so
/// they never count as the operator's: the replay info and tier text sources' text and file, the
/// visibility of the game scene items it shows and hides (info, tier, rank images), and each
/// report browser source's url, css (the match report scroll), and height.
/// </summary>
public sealed class ObsRuntimeValues
{
    public static readonly ObsRuntimeValues None = new();

    private static readonly string[] TextSettings =
    [
        "settings.file",
        "settings.text",
        "settings.read_from_file",
    ];

    private static readonly string[] ReportSettings =
    [
        "settings.url",
        "settings.css",
        "settings.height",
    ];

    private readonly HashSet<string> sources = new(StringComparer.Ordinal);
    private readonly HashSet<string> items = new(StringComparer.Ordinal);

    public static ObsRuntimeValues From(OBSSettings obs)
    {
        var values = new ObsRuntimeValues();
        if (obs == null)
        {
            return values;
        }

        // ObsController shows and hides these game scene items; a rank image's file is the
        // template's, so only the text sources' contents are the spectator's.
        foreach (
            ObsContractItem item in ObsContract
                .From(obs)
                .Items.Where(item =>
                    string.Equals(item.Scene, obs.GameSceneName, StringComparison.Ordinal)
                )
        )
        {
            values.items.Add(Key(item.Scene, item.Source, "visible"));
        }

        foreach (
            string text in new[]
            {
                obs.InfoSourceName,
                obs.TierDivisionSourceName,
                obs.TierRankPointsSourceName,
            }.Where(name => !string.IsNullOrWhiteSpace(name))
        )
        {
            foreach (string setting in TextSettings)
            {
                values.sources.Add(Key(text, setting));
            }
        }

        foreach (
            ReportScene scene in (obs.ReportScenes ?? []).Where(scene =>
                scene?.Enabled == true && !string.IsNullOrWhiteSpace(scene.SourceName)
            )
        )
        {
            foreach (string setting in ReportSettings)
            {
                values.sources.Add(Key(scene.SourceName, setting));
            }
        }

        return values;
    }

    internal bool SetsSource(string source, string property) =>
        sources.Contains(Key(source, property));

    internal bool SetsItem(string scene, string source, string property) =>
        items.Contains(Key(scene, source, property));

    private static string Key(params string[] parts) => string.Join('\u001f', parts);
}
