using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary>How much of the operator's work a merge may keep.</summary>
public enum ObsMergeScope
{
    /// <summary><c>obs apply</c>: every operator override, addition, and removal is kept.</summary>
    KeepOperatorChanges,

    /// <summary>
    /// An update (<c>update install-obs</c>, <c>services start</c>, the spectator): only a
    /// collection where the operator added scenes, sources, filters, or settings is merged. An
    /// override or a removal keeps the collection as it is, as before #307.
    /// </summary>
    AdditionsOnly,
}

/// <summary>What a merge did.</summary>
public enum ObsMergeOutcome
{
    /// <summary><see cref="ObsMergeResult.Merged"/> holds the merged collection.</summary>
    Merged,

    /// <summary>The template the live collection was last written from is not known.</summary>
    BaseUnknown,

    /// <summary>The template and the operator changed the same value.</summary>
    Conflict,

    /// <summary><see cref="ObsMergeScope.AdditionsOnly"/>, and the operator changed or removed something.</summary>
    OperatorChanges,

    /// <summary>The merged collection did not compare as the template plus the operator's work.</summary>
    Unverified,
}

/// <summary>The result of <see cref="ObsCollectionMerge.Merge"/>.</summary>
public sealed record ObsMergeResult
{
    public ObsMergeOutcome Outcome { get; init; }

    public bool Ok => Outcome == ObsMergeOutcome.Merged;

    /// <summary>
    /// The merged collection, with this install's paths; the live text itself when there was
    /// nothing to take from the template. Null when the merge was refused.
    /// </summary>
    public string Merged { get; init; }

    /// <summary>The diff the merge was made from.</summary>
    public ObsCollectionDiffResult Diff { get; init; }

    /// <summary>The differences that refused the merge.</summary>
    public IReadOnlyList<ObsCollectionDifference> Blocking { get; init; } = [];

    public string Message { get; init; }

    /// <summary>How many differences the merge took from the template.</summary>
    public int Taken =>
        Diff?.Differences.Count(difference => difference.Apply == ObsDiffApply.Template) ?? 0;

    /// <summary>How many differences the merge kept as the operator left them.</summary>
    public int Kept =>
        Diff?.Differences.Count(difference => difference.Apply == ObsDiffApply.Keep) ?? 0;
}

/// <summary>The template a live collection was last written from, and where it was found.</summary>
/// <param name="Text">The template, or null when it is not known.</param>
/// <param name="Source"><c>install</c>, <c>previous</c>, <c>stored</c> (<c>obs\templates</c>), or <c>none</c>.</param>
public sealed record ObsCollectionBase(string Text, string Source);

/// <summary>
/// A three-way merge of scene collections (#307): the live collection with the template's
/// changes since the base (<see cref="ObsCollectionDiff"/>) put into it, and everything else, the
/// operator's work, OBS ids, hotkeys, transitions, and global audio, kept as it is. A merge is
/// refused on a conflict, without a base, and when the result does not compare as the template
/// plus the operator's work, so a merge that is written always is that.
/// </summary>
public static class ObsCollectionMerge
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] SourceLists = ["sources", "groups"];

    private static readonly string[] CurrentScenes =
    [
        "current_scene",
        "current_program_scene",
        "current_preview_scene",
    ];

    /// <summary>The live collection's base: the template its record names, from this install, the previous one, or the store.</summary>
    public static ObsCollectionBase FindBase(
        ObsManagedCollection record,
        string template,
        string templateHash,
        string previousTemplate,
        ObsManagedFiles managed
    )
    {
        string recorded = record?.TemplateSha256;
        if (
            string.IsNullOrWhiteSpace(recorded)
            || string.Equals(
                recorded,
                ObsCollectionPatcher.UnknownTemplate,
                StringComparison.Ordinal
            )
        )
        {
            return new ObsCollectionBase(null, "none");
        }

        if (template != null && string.Equals(recorded, templateHash, StringComparison.Ordinal))
        {
            return new ObsCollectionBase(template, "install");
        }

        if (
            previousTemplate != null
            && string.Equals(
                recorded,
                ObsCollectionPatcher.HashOf(previousTemplate),
                StringComparison.Ordinal
            )
        )
        {
            return new ObsCollectionBase(previousTemplate, "previous");
        }

        string stored = managed?.ReadTemplate(recorded);
        return stored == null
            ? new ObsCollectionBase(null, "none")
            : new ObsCollectionBase(stored, "stored");
    }

    /// <summary>
    /// A collection with this install's asset and data paths, as every comparison and write uses
    /// it. <paramref name="movedFrom"/>: older asset folders whose paths move too (#330).
    /// </summary>
    public static string Normalize(
        string json,
        string assetRoot,
        string dataDirectory,
        IReadOnlyList<string> movedFrom = null
    ) =>
        json == null ? null : ObsCollectionPaths.Rewrite(json, assetRoot, dataDirectory, movedFrom);

    /// <param name="baseJson">The template the live collection was last written from, or null when not known.</param>
    /// <param name="templateJson">The template to merge in.</param>
    /// <param name="liveJson">The live collection.</param>
    /// <param name="runtime">Values HeroesReplay sets while it runs: neither compared nor merged.</param>
    /// <param name="scope">What of the operator's work a merge may keep.</param>
    /// <exception cref="JsonException">One of the documents is not valid JSON.</exception>
    public static ObsMergeResult Merge(
        string baseJson,
        string templateJson,
        string liveJson,
        ObsRuntimeValues runtime,
        ObsMergeScope scope
    )
    {
        ObsCollectionDiffResult diff = ObsCollectionDiff.Compare(
            baseJson,
            templateJson,
            liveJson,
            runtime
        );
        if (!diff.BaseKnown)
        {
            return new ObsMergeResult
            {
                Outcome = ObsMergeOutcome.BaseUnknown,
                Diff = diff,
                Message =
                    "The template the live collection was last written from is not known, so it cannot be told who changed what, and nothing was merged.",
            };
        }

        List<ObsCollectionDifference> conflicts = diff
            .Differences.Where(difference => difference.Kind == ObsDiffKind.Conflict)
            .ToList();
        if (conflicts.Count > 0)
        {
            return new ObsMergeResult
            {
                Outcome = ObsMergeOutcome.Conflict,
                Diff = diff,
                Blocking = conflicts,
                Message =
                    $"The template and the operator both changed {conflicts.Count} value(s), so nothing was merged: "
                    + List(conflicts)
                    + ".",
            };
        }

        List<ObsCollectionDifference> changes = diff
            .Differences.Where(difference =>
                difference.Kind is ObsDiffKind.OperatorOverride or ObsDiffKind.OperatorRemoval
            )
            .ToList();
        if (scope == ObsMergeScope.AdditionsOnly && changes.Count > 0)
        {
            return new ObsMergeResult
            {
                Outcome = ObsMergeOutcome.OperatorChanges,
                Diff = diff,
                Blocking = changes,
                Message =
                    $"The operator changed or removed {changes.Count} thing(s) the template has, and an update merges only into a collection where the operator added, so nothing was merged: "
                    + List(changes)
                    + ". Close OBS and run heroesreplay obs apply --backup to merge and keep them.",
            };
        }

        List<ObsCollectionDifference> take = diff
            .Differences.Where(difference => difference.Apply == ObsDiffApply.Template)
            .ToList();
        if (take.Count == 0)
        {
            return new ObsMergeResult
            {
                Outcome = ObsMergeOutcome.Merged,
                Diff = diff,
                Merged = liveJson,
                Message = "There is nothing to take from the template.",
            };
        }

        string merged;
        try
        {
            merged = new Writer(liveJson, templateJson).Apply(take);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            return Unverified(diff, e.Message);
        }

        // The merge must compare as the template plus exactly the operator's work it kept.
        ObsCollectionDiffResult check = ObsCollectionDiff.Compare(
            baseJson,
            templateJson,
            merged,
            runtime
        );
        List<ObsCollectionDifference> kept = diff
            .Differences.Where(difference => difference.Apply == ObsDiffApply.Keep)
            .ToList();
        if (!check.Differences.SequenceEqual(kept))
        {
            return Unverified(
                diff,
                "it differs from the template plus the operator's work at "
                    + List(check.Differences.Except(kept).Concat(kept.Except(check.Differences)))
            );
        }

        return new ObsMergeResult
        {
            Outcome = ObsMergeOutcome.Merged,
            Diff = diff,
            Merged = merged,
            Message =
                $"Takes {take.Count} template change(s) and keeps {kept.Count} of the operator's.",
        };
    }

    private static ObsMergeResult Unverified(ObsCollectionDiffResult diff, string why) =>
        new()
        {
            Outcome = ObsMergeOutcome.Unverified,
            Diff = diff,
            Message =
                "The merge did not check out ("
                + why
                + "), so nothing was merged. Merge the template's changes by hand in OBS.",
        };

    private static string List(IEnumerable<ObsCollectionDifference> differences)
    {
        List<ObsCollectionDifference> all = differences.ToList();
        return string.Join("; ", all.Take(8).Select(difference => difference.Describe()))
            + (all.Count > 8 ? $"; and {all.Count - 8} more" : string.Empty);
    }

    /// <summary>Puts the template's side of each difference into the live collection.</summary>
    private sealed class Writer
    {
        private readonly JsonObject live;
        private readonly JsonObject template;
        private readonly HashSet<string> added = new(StringComparer.Ordinal);
        private readonly List<JsonObject> inserted = [];

        public Writer(string liveJson, string templateJson)
        {
            live =
                JsonNode.Parse(liveJson) as JsonObject
                ?? throw new InvalidOperationException("the live collection is not an object");
            template =
                JsonNode.Parse(templateJson) as JsonObject
                ?? throw new InvalidOperationException("the template is not an object");
        }

        public string Apply(IReadOnlyList<ObsCollectionDifference> take)
        {
            foreach (
                ObsCollectionDifference difference in take.Where(difference =>
                    difference.Entity == ObsDiffEntity.Source
                    && difference.Property == null
                    && difference.Kind == ObsDiffKind.ManagedAddition
                )
            )
            {
                added.Add(difference.Source);
            }

            foreach (ObsCollectionDifference difference in take)
            {
                if (difference.Property == null)
                {
                    Entity(difference);
                }
                else
                {
                    Property(difference);
                }
            }

            PointItemsAtLiveSources();
            KeepCurrentScenes();
            return live.ToJsonString(Indented);
        }

        private void Entity(ObsCollectionDifference difference)
        {
            bool add = difference.Kind == ObsDiffKind.ManagedAddition;
            switch (difference.Entity)
            {
                case ObsDiffEntity.Source when add:
                    AddSource(difference.Source);
                    break;
                case ObsDiffEntity.Source:
                    RemoveSource(difference.Source);
                    break;
                case ObsDiffEntity.Filter when add:
                    AddFilter(difference.Source, difference.Filter);
                    break;
                case ObsDiffEntity.Filter:
                    if (Filter(live, difference.Source, difference.Filter) is JsonObject filter)
                    {
                        ((JsonArray)Find(live, difference.Source).Source["filters"]).Remove(filter);
                    }

                    break;
                case ObsDiffEntity.SceneItem when add:
                    InsertItem(
                        Find(live, difference.Scene).Source
                            ?? throw Missing("scene", difference.Scene),
                        Find(template, difference.Scene).Source
                            ?? throw Missing("template scene", difference.Scene),
                        difference.Source,
                        difference.Occurrence ?? 1
                    );
                    break;
                default:
                    if (
                        Item(live, difference.Scene, difference.Source, difference.Occurrence ?? 1)
                        is JsonObject item
                    )
                    {
                        Items(Find(live, difference.Scene).Source).Remove(item);
                    }

                    break;
            }
        }

        private void Property(ObsCollectionDifference difference)
        {
            (JsonObject liveEntity, JsonObject templateEntity) = difference.Entity switch
            {
                ObsDiffEntity.Filter => (
                    Filter(live, difference.Source, difference.Filter),
                    Filter(template, difference.Source, difference.Filter)
                ),
                ObsDiffEntity.SceneItem => (
                    Item(live, difference.Scene, difference.Source, difference.Occurrence ?? 1),
                    Item(template, difference.Scene, difference.Source, difference.Occurrence ?? 1)
                ),
                _ => (
                    Find(live, difference.Source).Source,
                    Find(template, difference.Source).Source
                ),
            };
            if (liveEntity == null || templateEntity == null)
            {
                throw new InvalidOperationException(
                    "'" + difference.Describe() + "' is not in both collections"
                );
            }

            if (difference.Entity != ObsDiffEntity.SceneItem && difference.Property == "kind")
            {
                // The diff reads a source's or filter's kind from its "id".
                foreach (string key in new[] { "id", "versioned_id" })
                {
                    if (templateEntity[key] is JsonNode kind)
                    {
                        liveEntity[key] = kind.DeepClone();
                    }
                }

                return;
            }

            if (difference.Template == null)
            {
                Remove(liveEntity, templateEntity, difference.Property);
            }
            else
            {
                Set(liveEntity, templateEntity, difference.Property);
            }
        }

        private void AddSource(string name)
        {
            var (_, source, list) = Find(template, name);
            if (source == null)
            {
                throw Missing("template source", name);
            }

            JsonObject copy = source.DeepClone().AsObject();
            if (Uuids(live).Contains(StringOf(copy, "uuid")))
            {
                copy["uuid"] = Guid.NewGuid().ToString();
            }

            if (live[list] is not JsonArray sources)
            {
                sources = [];
                live[list] = sources;
            }

            sources.Add(copy);
            if (IsScene(copy))
            {
                inserted.AddRange(Items(copy)?.OfType<JsonObject>() ?? []);
                AddToSceneOrder(name);
            }

            // Its placements in scenes the live collection already has. A scene the template
            // adds brings its own; a scene the operator removed stays removed.
            foreach (
                JsonObject scene in Scenes(template).Where(scene => !added.Contains(NameOf(scene)))
            )
            {
                JsonObject liveScene = Find(live, NameOf(scene)).Source;
                if (liveScene == null || !IsScene(liveScene))
                {
                    continue;
                }

                int count = Items(scene).OfType<JsonObject>().Count(item => NameOf(item) == name);
                for (int occurrence = 1; occurrence <= count; occurrence++)
                {
                    InsertItem(liveScene, scene, name, occurrence);
                }
            }
        }

        private void RemoveSource(string name)
        {
            var (sources, source, _) = Find(live, name);
            if (source == null)
            {
                return;
            }

            sources.Remove(source);
            foreach (JsonObject scene in Scenes(live))
            {
                JsonArray items = Items(scene);
                foreach (
                    JsonObject item in items
                        .OfType<JsonObject>()
                        .Where(item => NameOf(item) == name)
                        .ToList()
                )
                {
                    items.Remove(item);
                }
            }

            if (live["scene_order"] is JsonArray order)
            {
                foreach (JsonNode entry in order.Where(entry => NameOf(entry) == name).ToList())
                {
                    order.Remove(entry);
                }
            }
        }

        private void AddFilter(string sourceName, string filterName)
        {
            JsonObject source =
                Find(live, sourceName).Source ?? throw Missing("source", sourceName);
            JsonArray templateFilters =
                Find(template, sourceName).Source?["filters"] as JsonArray
                ?? throw Missing("template filters of", sourceName);
            JsonObject filter =
                Filter(template, sourceName, filterName)
                ?? throw Missing("template filter", filterName);
            if (source["filters"] is not JsonArray filters)
            {
                filters = [];
                source["filters"] = filters;
            }

            JsonObject copy = filter.DeepClone().AsObject();
            if (
                filters
                    .OfType<JsonObject>()
                    .Any(other => StringOf(other, "uuid") == StringOf(copy, "uuid"))
            )
            {
                copy["uuid"] = Guid.NewGuid().ToString();
            }

            // After the nearest filter before it in the template that the live source has: filters run in order.
            int at = templateFilters.IndexOf(filter);
            int position = 0;
            for (int earlier = at - 1; earlier >= 0; earlier--)
            {
                JsonNode before = filters.FirstOrDefault(other =>
                    NameOf(other) == NameOf(templateFilters[earlier])
                );
                if (before != null)
                {
                    position = filters.IndexOf(before) + 1;
                    break;
                }
            }

            filters.Insert(position, copy);
        }

        /// <summary>
        /// The template's placement of <paramref name="source"/> goes into the live scene, above the
        /// nearest item below it in the template that the live scene has, with a new item id.
        /// </summary>
        private void InsertItem(
            JsonObject liveScene,
            JsonObject templateScene,
            string source,
            int occurrence
        )
        {
            JsonArray templateItems =
                Items(templateScene) ?? throw Missing("template items of", NameOf(templateScene));
            JsonObject item =
                Nth(templateItems, source, occurrence) ?? throw Missing("template item", source);
            if (liveScene["settings"] is not JsonObject settings)
            {
                settings = [];
                liveScene["settings"] = settings;
            }

            if (settings["items"] is not JsonArray items)
            {
                items = [];
                settings["items"] = items;
            }

            int position = 0;
            int at = templateItems.IndexOf(item);
            for (int earlier = at - 1; earlier >= 0; earlier--)
            {
                string below = NameOf(templateItems[earlier]);
                int count = templateItems.Take(earlier + 1).Count(other => NameOf(other) == below);
                JsonObject found = Nth(items, below, count);
                if (found != null)
                {
                    position = items.IndexOf(found) + 1;
                    break;
                }
            }

            long id =
                Math.Max(
                    LongOf(settings["id_counter"]),
                    items
                        .OfType<JsonObject>()
                        .Select(other => LongOf(other["id"]))
                        .DefaultIfEmpty(0)
                        .Max()
                ) + 1;
            JsonObject copy = item.DeepClone().AsObject();
            copy["id"] = id;
            settings["id_counter"] = id;
            items.Insert(position, copy);
            inserted.Add(copy);
        }

        private void AddToSceneOrder(string scene)
        {
            if (live["scene_order"] is not JsonArray order)
            {
                return;
            }

            int position = order.Count;
            if (template["scene_order"] is JsonArray templateOrder)
            {
                int at = templateOrder.Select(NameOf).ToList().IndexOf(scene);
                position = at < 0 ? order.Count : 0;
                for (int earlier = at - 1; earlier >= 0; earlier--)
                {
                    JsonNode before = order.FirstOrDefault(entry =>
                        NameOf(entry) == NameOf(templateOrder[earlier])
                    );
                    if (before != null)
                    {
                        position = order.IndexOf(before) + 1;
                        break;
                    }
                }
            }

            order.Insert(position, new JsonObject { ["name"] = scene });
        }

        /// <summary>An item the merge placed shows the live source of its name (OBS finds it by uuid first).</summary>
        private void PointItemsAtLiveSources()
        {
            foreach (JsonObject item in inserted)
            {
                string uuid = StringOf(Find(live, NameOf(item)).Source, "uuid");
                if (uuid != null)
                {
                    item["source_uuid"] = uuid;
                }
            }
        }

        /// <summary>A current scene the merge removed becomes the template's, or the first scene.</summary>
        private void KeepCurrentScenes()
        {
            var scenes = Scenes(live).Select(NameOf).ToHashSet(StringComparer.Ordinal);
            foreach (string key in CurrentScenes)
            {
                string current = StringOf(live, key);
                if (current == null || scenes.Contains(current))
                {
                    continue;
                }

                string fallback = StringOf(template, key);
                live[key] = scenes.Contains(fallback ?? string.Empty)
                    ? fallback
                    : (live["scene_order"] as JsonArray)
                        ?.Select(NameOf)
                        .FirstOrDefault(scenes.Contains)
                        ?? scenes.FirstOrDefault();
            }
        }

        /// <summary>Sets a dotted property to the template's value, making the objects on the way.</summary>
        private static void Set(JsonObject liveEntity, JsonObject templateEntity, string property)
        {
            List<string> path =
                Resolve(templateEntity, property)
                ?? throw new InvalidOperationException("the template has no " + property);
            JsonNode value = At(templateEntity, path);
            JsonObject current = liveEntity;
            foreach (string key in path.Take(path.Count - 1))
            {
                if (!current.TryGetPropertyValue(key, out JsonNode next))
                {
                    var made = new JsonObject();
                    current[key] = made;
                    current = made;
                }
                else
                {
                    current =
                        next as JsonObject
                        ?? throw new InvalidOperationException(
                            "the live " + property + " is not an object where the template's is"
                        );
                }
            }

            current[path[^1]] = value?.DeepClone();
        }

        /// <summary>
        /// Removes a dotted property, and each object on the way that it leaves empty and the
        /// template does not have.
        /// </summary>
        private static void Remove(
            JsonObject liveEntity,
            JsonObject templateEntity,
            string property
        )
        {
            List<string> path = Resolve(liveEntity, property);
            if (path == null)
            {
                return;
            }

            var parents = new List<JsonObject> { liveEntity };
            foreach (string key in path.Take(path.Count - 1))
            {
                parents.Add((JsonObject)parents[^1][key]);
            }

            parents[^1].Remove(path[^1]);
            for (int depth = path.Count - 2; depth >= 0; depth--)
            {
                JsonObject emptied = parents[depth + 1];
                if (emptied.Count > 0 || At(templateEntity, path.Take(depth + 1).ToList()) != null)
                {
                    break;
                }

                parents[depth].Remove(path[depth]);
            }
        }

        /// <summary>
        /// The keys of a dotted property in <paramref name="entity"/>. A key may hold a dot, so the
        /// longest key that fits is taken at each level. Null when the property is not there.
        /// </summary>
        private static List<string> Resolve(JsonObject entity, string property)
        {
            var path = new List<string>();
            JsonObject current = entity;
            string rest = property;
            while (current != null)
            {
                string key = current
                    .Select(pair => pair.Key)
                    .Where(name =>
                        rest == name || rest.StartsWith(name + ".", StringComparison.Ordinal)
                    )
                    .OrderByDescending(name => name.Length)
                    .FirstOrDefault();
                if (key == null)
                {
                    return null;
                }

                path.Add(key);
                if (rest.Length == key.Length)
                {
                    return path;
                }

                current = current[key] as JsonObject;
                rest = rest[(key.Length + 1)..];
            }

            return null;
        }

        private static JsonNode At(JsonObject entity, IReadOnlyList<string> path)
        {
            JsonNode current = entity;
            foreach (string key in path)
            {
                if (
                    current is not JsonObject parent
                    || !parent.TryGetPropertyValue(key, out current)
                )
                {
                    return null;
                }
            }

            return current;
        }

        private static (JsonArray List, JsonObject Source, string ListName) Find(
            JsonObject root,
            string name
        )
        {
            foreach (string list in SourceLists)
            {
                if (root[list] is JsonArray sources)
                {
                    JsonObject source = sources
                        .OfType<JsonObject>()
                        .FirstOrDefault(entry => NameOf(entry) == name);
                    if (source != null)
                    {
                        return (sources, source, list);
                    }
                }
            }

            return (null, null, "sources");
        }

        private static JsonObject Filter(JsonObject root, string source, string filter) =>
            (Find(root, source).Source?["filters"] as JsonArray)
                ?.OfType<JsonObject>()
                .FirstOrDefault(entry => NameOf(entry) == filter);

        private static JsonObject Item(
            JsonObject root,
            string scene,
            string source,
            int occurrence
        ) => Nth(Items(Find(root, scene).Source), source, occurrence);

        private static JsonObject Nth(JsonArray items, string source, int occurrence) =>
            items
                ?.OfType<JsonObject>()
                .Where(item => NameOf(item) == source)
                .Skip(occurrence - 1)
                .FirstOrDefault();

        private static JsonArray Items(JsonObject scene) =>
            scene?["settings"]?["items"] as JsonArray;

        private static IEnumerable<JsonObject> Scenes(JsonObject root) =>
            SourceLists
                .Select(list => root[list] as JsonArray)
                .Where(sources => sources != null)
                .SelectMany(sources => sources.OfType<JsonObject>())
                .Where(IsScene)
                .ToList();

        private static bool IsScene(JsonObject source) =>
            StringOf(source, "id") is "scene" or "group";

        private static HashSet<string> Uuids(JsonObject root) =>
            SourceLists
                .Select(list => root[list] as JsonArray)
                .Where(sources => sources != null)
                .SelectMany(sources => sources.OfType<JsonObject>())
                .Select(source => StringOf(source, "uuid"))
                .Where(uuid => uuid != null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static string NameOf(JsonNode node) => StringOf(node as JsonObject, "name");

        private static string StringOf(JsonObject node, string property) =>
            node?[property] is JsonValue value && value.TryGetValue(out string text) ? text : null;

        private static long LongOf(JsonNode node) =>
            node is JsonValue value && value.TryGetValue(out long number) ? number : 0;

        private static InvalidOperationException Missing(string what, string name) =>
            new($"the {what} '{name}' was not found");
    }
}
