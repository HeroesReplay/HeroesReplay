using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs;

/// <summary>Typed reads of obs-websocket responseData that tolerate a missing field.</summary>
internal static class ObsResponse
{
    public static string String(JToken token, string name) =>
        token?[name]?.Type == JTokenType.String ? (string)token[name] : null;

    public static bool? Bool(JToken token, string name) =>
        token?[name]?.Type == JTokenType.Boolean ? (bool)token[name] : null;

    public static long? Long(JToken token, string name) =>
        token?[name]?.Type is JTokenType.Integer or JTokenType.Float ? (long)token[name] : null;

    public static double? Double(JToken token, string name) =>
        token?[name]?.Type is JTokenType.Integer or JTokenType.Float ? (double)token[name] : null;

    public static IEnumerable<JObject> Objects(JToken token, string name) =>
        (token?[name] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();

    public static IReadOnlyList<string> Strings(JToken token, string name) =>
        (token?[name] as JArray)
            ?.Where(item => item.Type == JTokenType.String)
            .Select(item => (string)item)
            .ToList()
        ?? new List<string>();
}
