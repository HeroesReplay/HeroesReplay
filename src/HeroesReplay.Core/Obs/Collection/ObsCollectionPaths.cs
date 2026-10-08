using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using HeroesReplay.Core.Obs.Inspection;

namespace HeroesReplay.Core.Obs.Collection;

public static class ObsCollectionPaths
{
    public const string CheckoutMarker = "heroesreplay/HeroesReplay";
    private const string CheckoutPrefix = "C:/heroesreplay/HeroesReplay/obs";
    private const string DataPrefix = "C:/heroesreplay/Data";

    /// <summary>A path with a <c>worktrees\&lt;name&gt;\</c> folder in it (forward slashes).</summary>
    private static readonly Regex WorktreeFolder = new(
        @"/worktrees/[^/]+/",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    /// <summary>A file in a git worktree's packaged <c>obs</c> folder (forward slashes).</summary>
    private static readonly Regex WorktreeObsAsset = new(
        @"^.*/worktrees/[^/]+/obs/(?<relative>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    private static readonly Regex LocalReference = new Regex(
        @"(?:(?:src|href)\s*=\s*[""'](?<path>[^""']+)[""']|imageURL\s*=\s*[""'](?<path>[^""']+)[""']|url\(\s*[""']?(?<path>[^""')]+)[""']?\s*\))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    public static bool ContainsCheckoutPath(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        return json.Contains(CheckoutMarker, StringComparison.OrdinalIgnoreCase)
            || json.Contains(@"heroesreplay\HeroesReplay", StringComparison.OrdinalIgnoreCase)
            || json.Contains(@"heroesreplay\\HeroesReplay", StringComparison.OrdinalIgnoreCase);
    }

    public static string FindCollection(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return null;
        }

        var dir = new DirectoryInfo(startDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "obs", "Default.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    public static IReadOnlyList<string> RelativeAssets(string json)
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return found.ToList();
        }

        using JsonDocument document = JsonDocument.Parse(json);
        Walk(
            document.RootElement,
            (_, element) =>
            {
                if (TryRelativeAsset(element.GetString(), out string relative))
                {
                    found.Add(relative);
                }
            }
        );
        return found.ToList();
    }

    public static IReadOnlyList<string> PackageRelativePaths(string obsDirectory, string json)
    {
        var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        foreach (string asset in RelativeAssets(json))
        {
            if (result.Add(NormalizeRelative(asset)))
            {
                pending.Enqueue(NormalizeRelative(asset));
            }
        }

        var scanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.Count > 0)
        {
            string relative = pending.Dequeue();
            if (!scanned.Add(relative) || !IsTextAsset(Path.GetExtension(relative)))
            {
                continue;
            }

            string full = ToFullPath(obsDirectory, relative);
            if (!File.Exists(full))
            {
                continue;
            }

            foreach (Match match in LocalReference.Matches(File.ReadAllText(full)))
            {
                string child = ResolveUnderObs(obsDirectory, full, match.Groups["path"].Value);
                if (child != null && result.Add(child))
                {
                    pending.Enqueue(child);
                }
            }
        }

        return result.ToList();
    }

    public static IReadOnlyList<string> MissingAssets(string obsDirectory, string json)
    {
        return PackageRelativePaths(obsDirectory, json)
            .Where(relative => !File.Exists(ToFullPath(obsDirectory, relative)))
            .ToList();
    }

    public static IReadOnlyList<string> SourceNames(string json) =>
        NamedSources(json, scenesOnly: false);

    public static IReadOnlyList<string> SceneNames(string json) =>
        NamedSources(json, scenesOnly: true);

    /// <summary>Scene name → the names of the items in it, bottom first, as OBS saves them.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> SceneItemNames(string json)
    {
        var scenes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return scenes;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (
            !document.RootElement.TryGetProperty("sources", out JsonElement sources)
            || sources.ValueKind != JsonValueKind.Array
        )
        {
            return scenes;
        }

        foreach (JsonElement source in sources.EnumerateArray())
        {
            if (
                !source.TryGetProperty("id", out JsonElement id)
                || id.ValueKind != JsonValueKind.String
                || !string.Equals(id.GetString(), "scene", StringComparison.Ordinal)
                || !source.TryGetProperty("name", out JsonElement name)
                || name.ValueKind != JsonValueKind.String
            )
            {
                continue;
            }

            var items = new List<string>();
            if (
                source.TryGetProperty("settings", out JsonElement settings)
                && settings.ValueKind == JsonValueKind.Object
                && settings.TryGetProperty("items", out JsonElement list)
                && list.ValueKind == JsonValueKind.Array
            )
            {
                foreach (JsonElement item in list.EnumerateArray())
                {
                    if (
                        item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("name", out JsonElement itemName)
                        && itemName.ValueKind == JsonValueKind.String
                    )
                    {
                        items.Add(itemName.GetString());
                    }
                }
            }

            scenes.TryAdd(name.GetString(), items);
        }

        return scenes;
    }

    /// <summary>
    /// Scene name → item name → where the collection places that item (the first item of that
    /// name in the scene).
    /// </summary>
    public static IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, ObsPlacement>
    > ScenePlacements(string json)
    {
        var scenes = new Dictionary<string, IReadOnlyDictionary<string, ObsPlacement>>(
            StringComparer.Ordinal
        );
        if (string.IsNullOrWhiteSpace(json))
        {
            return scenes;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (
            !document.RootElement.TryGetProperty("sources", out JsonElement sources)
            || sources.ValueKind != JsonValueKind.Array
        )
        {
            return scenes;
        }

        foreach (JsonElement source in sources.EnumerateArray())
        {
            if (
                !source.TryGetProperty("id", out JsonElement id)
                || id.ValueKind != JsonValueKind.String
                || !string.Equals(id.GetString(), "scene", StringComparison.Ordinal)
                || !source.TryGetProperty("name", out JsonElement name)
                || name.ValueKind != JsonValueKind.String
                || !source.TryGetProperty("settings", out JsonElement settings)
                || settings.ValueKind != JsonValueKind.Object
                || !settings.TryGetProperty("items", out JsonElement list)
                || list.ValueKind != JsonValueKind.Array
            )
            {
                continue;
            }

            var items = new Dictionary<string, ObsPlacement>(StringComparer.Ordinal);
            foreach (JsonElement item in list.EnumerateArray())
            {
                if (
                    item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("name", out JsonElement itemName)
                    && itemName.ValueKind == JsonValueKind.String
                )
                {
                    items.TryAdd(itemName.GetString(), ObsPlacement.FromCollection(item));
                }
            }

            scenes.TryAdd(name.GetString(), items);
        }

        return scenes;
    }

    public static IReadOnlyList<string> MissingScenes(string json, IEnumerable<string> required) =>
        MissingNames(SceneNames(json), required);

    public static IReadOnlyList<string> MissingSources(string json, IEnumerable<string> required) =>
        MissingNames(SourceNames(json), required);

    /// <summary>The filters each source has in the collection: source name → (filter name, filter kind).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<ObsFilterInfo>> SourceFilters(
        string json
    )
    {
        var filters = new Dictionary<string, IReadOnlyList<ObsFilterInfo>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return filters;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (
            !document.RootElement.TryGetProperty("sources", out JsonElement sources)
            || sources.ValueKind != JsonValueKind.Array
        )
        {
            return filters;
        }

        foreach (JsonElement source in sources.EnumerateArray())
        {
            if (
                !source.TryGetProperty("name", out JsonElement name)
                || name.ValueKind != JsonValueKind.String
                || !source.TryGetProperty("filters", out JsonElement list)
                || list.ValueKind != JsonValueKind.Array
            )
            {
                continue;
            }

            var named = new List<ObsFilterInfo>();
            foreach (JsonElement filter in list.EnumerateArray())
            {
                if (
                    filter.TryGetProperty("name", out JsonElement filterName)
                    && filterName.ValueKind == JsonValueKind.String
                )
                {
                    named.Add(
                        new ObsFilterInfo(
                            filterName.GetString(),
                            filter.TryGetProperty("id", out JsonElement kind)
                            && kind.ValueKind == JsonValueKind.String
                                ? kind.GetString()
                                : null
                        )
                    );
                }
            }

            if (named.Count > 0)
            {
                filters[name.GetString()] = named;
            }
        }

        return filters;
    }

    /// <summary>Source name → unversioned kind (<c>id</c>), such as <c>browser_source</c>.</summary>
    public static IReadOnlyDictionary<string, string> SourceKinds(string json)
    {
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return kinds;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (
            !document.RootElement.TryGetProperty("sources", out JsonElement sources)
            || sources.ValueKind != JsonValueKind.Array
        )
        {
            return kinds;
        }

        foreach (JsonElement source in sources.EnumerateArray())
        {
            if (
                source.TryGetProperty("name", out JsonElement name)
                && name.ValueKind == JsonValueKind.String
                && source.TryGetProperty("id", out JsonElement id)
                && id.ValueKind == JsonValueKind.String
            )
            {
                kinds.TryAdd(name.GetString(), id.GetString());
            }
        }

        return kinds;
    }

    /// <summary>
    /// True for a folder inside a git worktree (<c>...\.claude\worktrees\&lt;name&gt;\</c>,
    /// <c>...\worktrees\&lt;name&gt;\</c>). Agents and release candidates build there, and the
    /// folder is deleted with the worktree, so OBS must never be pointed at it (#330).
    /// </summary>
    public static bool IsEphemeral(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && WorktreeFolder.IsMatch(path.Replace('\\', '/').TrimEnd('/') + "/");

    public static string Rewrite(string json, string assetRoot, string dataDirectory) =>
        Rewrite(json, assetRoot, dataDirectory, null);

    /// <summary>
    /// Points the collection's asset paths at <paramref name="assetRoot"/> and its data paths at
    /// <paramref name="dataDirectory"/>. Besides relative paths and the checkout's
    /// <c>obs</c> folder, an asset path under a git worktree's <c>obs</c> folder, or under one of
    /// <paramref name="movedFrom"/> (a folder, or <c>folder\*</c> for any folder in it), is
    /// moved too (#330).
    /// </summary>
    public static string Rewrite(
        string json,
        string assetRoot,
        string dataDirectory,
        IReadOnlyList<string> movedFrom
    )
    {
        if (string.IsNullOrEmpty(json))
        {
            return json;
        }

        var replacements = new List<(string Old, string New)>();
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            Walk(
                document.RootElement,
                (name, element) =>
                {
                    string logical = element.GetString();
                    string updated = RewriteValue(
                        name,
                        logical,
                        assetRoot,
                        dataDirectory,
                        movedFrom
                    );
                    if (string.Equals(updated, logical, StringComparison.Ordinal))
                    {
                        return;
                    }

                    string raw = element.GetRawText();
                    string encoded = JsonSerializer.Serialize(updated);
                    if (!string.Equals(raw, encoded, StringComparison.Ordinal))
                    {
                        replacements.Add((raw, encoded));
                    }
                }
            );
        }

        string result = json;
        foreach (
            (string old, string updated) in replacements
                .DistinctBy(pair => pair.Old)
                .OrderByDescending(pair => pair.Old.Length)
        )
        {
            result = result.Replace(old, updated, StringComparison.Ordinal);
        }

        return result;
    }

    public static string RewriteValue(
        string propertyName,
        string value,
        string assetRoot,
        string dataDirectory,
        IReadOnlyList<string> movedFrom = null
    )
    {
        if (!TrySplitLocal(value, out string path, out string query, out bool fileUrl))
        {
            return value;
        }

        string absolute = null;
        if (
            TryCheckoutRelative(path, out string fromCheckout)
            || TryMovedRelative(path, movedFrom, out fromCheckout)
            || IsRelativeAsset(path)
        )
        {
            if (string.IsNullOrWhiteSpace(assetRoot))
            {
                return value;
            }

            absolute = CombineForward(assetRoot, fromCheckout ?? path);
        }
        else if (TryDataRelative(path, out string fromData))
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                return value;
            }

            absolute = CombineForward(dataDirectory, fromData);
        }
        else
        {
            return value;
        }

        string updated = FormatPath(propertyName, fileUrl, absolute, query);
        return SameLocation(value, updated) ? value : updated;
    }

    public static ObsCollectionInspection Inspect(
        string startDirectory,
        IEnumerable<string> requiredScenes,
        IEnumerable<string> requiredSources
    )
    {
        string collection = FindCollection(startDirectory);
        if (collection == null)
        {
            return new ObsCollectionInspection(
                false,
                false,
                false,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                "OBS collection was not found at obs/Default.json."
            );
        }

        string obsDirectory = Path.GetDirectoryName(collection);
        string json;
        try
        {
            json = File.ReadAllText(collection);
        }
        catch (IOException e)
        {
            return new ObsCollectionInspection(
                true,
                false,
                false,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                "OBS collection could not be read. " + e.Message
            );
        }

        try
        {
            bool profileExists = File.Exists(Path.Combine(obsDirectory, "Default", "basic.ini"));
            bool checkout = ContainsCheckoutPath(json);
            IReadOnlyList<string> missingAssets = MissingAssets(obsDirectory, json);
            IReadOnlyList<string> missingScenes = MissingScenes(json, requiredScenes);
            IReadOnlyList<string> missingSources = MissingSources(json, requiredSources);
            string message = Describe(
                profileExists,
                checkout,
                missingAssets,
                missingScenes,
                missingSources
            );
            return new ObsCollectionInspection(
                true,
                profileExists,
                checkout,
                missingAssets,
                missingScenes,
                missingSources,
                message
            );
        }
        catch (JsonException)
        {
            return new ObsCollectionInspection(
                true,
                File.Exists(Path.Combine(obsDirectory, "Default", "basic.ini")),
                ContainsCheckoutPath(json),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                "OBS collection is not valid JSON."
            );
        }
    }

    private static string Describe(
        bool profileExists,
        bool checkout,
        IReadOnlyList<string> missingAssets,
        IReadOnlyList<string> missingScenes,
        IReadOnlyList<string> missingSources
    )
    {
        var parts = new List<string>();
        if (checkout)
        {
            parts.Add("The collection still points at C:\\heroesreplay\\HeroesReplay.");
        }

        if (!profileExists)
        {
            parts.Add("OBS profile basic.ini was not found.");
        }

        if (missingAssets.Count > 0)
        {
            parts.Add("Missing OBS assets: " + JoinLimited(missingAssets) + ".");
        }

        if (missingScenes.Count > 0)
        {
            parts.Add("Missing OBS scenes: " + JoinLimited(missingScenes) + ".");
        }

        if (missingSources.Count > 0)
        {
            parts.Add("Missing OBS sources: " + JoinLimited(missingSources) + ".");
        }

        return parts.Count == 0 ? "OBS scene files are in the install." : string.Join(" ", parts);
    }

    private static string JoinLimited(IReadOnlyList<string> names)
    {
        const int limit = 12;
        string shown = string.Join(", ", names.Take(limit));
        if (names.Count > limit)
        {
            shown += " (+" + (names.Count - limit) + " more)";
        }

        return shown;
    }

    private static IReadOnlyList<string> NamedSources(string json, bool scenesOnly)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return names;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (
            !document.RootElement.TryGetProperty("sources", out JsonElement sources)
            || sources.ValueKind != JsonValueKind.Array
        )
        {
            return names;
        }

        foreach (JsonElement source in sources.EnumerateArray())
        {
            if (scenesOnly)
            {
                if (
                    !source.TryGetProperty("id", out JsonElement id)
                    || !string.Equals(id.GetString(), "scene", StringComparison.Ordinal)
                )
                {
                    continue;
                }
            }

            if (
                source.TryGetProperty("name", out JsonElement name)
                && name.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(name.GetString())
            )
            {
                names.Add(name.GetString());
            }
        }

        return names;
    }

    public static IReadOnlyList<string> MissingNames(
        IReadOnlyList<string> present,
        IEnumerable<string> required
    )
    {
        var have = new HashSet<string>(present ?? Array.Empty<string>(), StringComparer.Ordinal);
        var missing = new List<string>();
        if (required == null)
        {
            return missing;
        }

        foreach (string name in required)
        {
            if (!string.IsNullOrWhiteSpace(name) && !have.Contains(name))
            {
                missing.Add(name);
            }
        }

        return missing;
    }

    private static bool TryRelativeAsset(string value, out string relative)
    {
        relative = null;
        if (!TrySplitLocal(value, out string path, out _, out _))
        {
            return false;
        }

        if (TryCheckoutRelative(path, out string fromCheckout))
        {
            relative = fromCheckout;
            return true;
        }

        if (TryDataRelative(path, out _) || !IsRelativeAsset(path))
        {
            return false;
        }

        relative = NormalizeRelative(path);
        return true;
    }

    private static bool TrySplitLocal(
        string value,
        out string path,
        out string query,
        out bool fileUrl
    )
    {
        path = null;
        query = string.Empty;
        fileUrl = false;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string text = value.Trim();
        if (
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        if (text.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            fileUrl = true;
            text = text.Substring("file:///".Length);
        }

        int queryIndex = text.IndexOf('?');
        if (queryIndex >= 0)
        {
            query = text.Substring(queryIndex);
            text = text.Substring(0, queryIndex);
        }

        path = text.Replace('\\', '/');
        return path.Length > 0;
    }

    private static bool TryCheckoutRelative(string path, out string relative)
    {
        relative = null;
        string normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith(CheckoutPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        relative = normalized.Substring(CheckoutPrefix.Length).TrimStart('/');
        return relative.Length > 0 && IsRelativeAsset(relative);
    }

    /// <summary>
    /// An asset path under a git worktree's <c>obs</c> folder, or under one of
    /// <paramref name="movedFrom"/>: the path inside it, when that is a packaged asset.
    /// </summary>
    private static bool TryMovedRelative(
        string path,
        IReadOnlyList<string> movedFrom,
        out string relative
    )
    {
        relative = null;
        string normalized = path.Replace('\\', '/');
        Match worktree = WorktreeObsAsset.Match(normalized);
        if (worktree.Success)
        {
            relative = worktree.Groups["relative"].Value;
        }

        foreach (string root in movedFrom ?? [])
        {
            if (relative != null || string.IsNullOrWhiteSpace(root))
            {
                break;
            }

            string prefix = root.Replace('\\', '/').TrimEnd('/');
            bool anyChild = prefix.EndsWith("/*", StringComparison.Ordinal);
            prefix = (anyChild ? prefix[..^2] : prefix) + "/";
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string rest = normalized[prefix.Length..];
            if (anyChild)
            {
                int slash = rest.IndexOf('/');
                rest = slash > 0 ? rest[(slash + 1)..] : string.Empty;
            }

            relative = rest.Length > 0 ? rest : null;
        }

        if (relative != null && !IsRelativeAsset(relative))
        {
            relative = null;
        }

        return relative != null;
    }

    private static bool TryDataRelative(string path, out string relative)
    {
        relative = null;
        string normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith(DataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        relative = normalized.Substring(DataPrefix.Length).TrimStart('/');
        return relative.Length > 0;
    }

    private static bool IsRelativeAsset(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (
            path.StartsWith("/", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal)
        )
        {
            return false;
        }

        if (path.Length >= 2 && path[1] == ':')
        {
            return false;
        }

        return IsAssetExtension(Path.GetExtension(path));
    }

    private static bool IsAssetExtension(string extension)
    {
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".css", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".svg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextAsset(string extension)
    {
        return extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".css", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".js", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPath(
        string propertyName,
        bool fileUrl,
        string absoluteForward,
        string query
    )
    {
        bool asUrl =
            fileUrl || string.Equals(propertyName, "url", StringComparison.OrdinalIgnoreCase);
        if (asUrl)
        {
            return "file:///" + absoluteForward + query;
        }

        return absoluteForward + query;
    }

    private static string CombineForward(string root, string relative)
    {
        return root.Replace('\\', '/').TrimEnd('/')
            + "/"
            + relative.Replace('\\', '/').TrimStart('/');
    }

    private static bool SameLocation(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        if (!TrySplitLocal(left, out string leftPath, out string leftQuery, out _))
        {
            return false;
        }

        if (!TrySplitLocal(right, out string rightPath, out string rightQuery, out _))
        {
            return false;
        }

        return string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(leftQuery, rightQuery, StringComparison.Ordinal);
    }

    private static string NormalizeRelative(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string ToFullPath(string obsDirectory, string relative)
    {
        return Path.Combine(
            obsDirectory,
            NormalizeRelative(relative).Replace('/', Path.DirectorySeparatorChar)
        );
    }

    private static string ResolveUnderObs(string obsDirectory, string fromFile, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        string text = reference.Trim();
        int hash = text.IndexOf('#');
        if (hash >= 0)
        {
            text = text.Substring(0, hash);
        }

        int query = text.IndexOf('?');
        if (query >= 0)
        {
            text = text.Substring(0, query);
        }

        if (
            text.Length == 0
            || text.Contains("://", StringComparison.Ordinal)
            || text.StartsWith("//", StringComparison.Ordinal)
        )
        {
            return null;
        }

        if (
            text.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || (text.Length >= 2 && text[1] == ':')
        )
        {
            return null;
        }

        string combined = Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(fromFile),
                text.Replace('/', Path.DirectorySeparatorChar)
            )
        );
        string root = Path.GetFullPath(obsDirectory);
        string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetRelativePath(root, combined).Replace('\\', '/');
    }

    private static void Walk(JsonElement element, Action<string, JsonElement> onString)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        onString(property.Name, property.Value);
                    }
                    else
                    {
                        Walk(property.Value, onString);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Walk(item, onString);
                }

                break;
        }
    }
}

public sealed record ObsCollectionInspection(
    bool Found,
    bool ProfileExists,
    bool ContainsCheckoutPath,
    IReadOnlyList<string> MissingAssets,
    IReadOnlyList<string> MissingScenes,
    IReadOnlyList<string> MissingSources,
    string Message
)
{
    public bool Ok =>
        Found
        && ProfileExists
        && !ContainsCheckoutPath
        && MissingAssets.Count == 0
        && MissingScenes.Count == 0
        && MissingSources.Count == 0;
}
