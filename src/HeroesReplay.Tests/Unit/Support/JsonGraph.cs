using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HeroesReplay.Tests.Unit.Support;

/// <summary>
/// The types a JSON file's metadata reaches from its root (#454): every property type, list item,
/// and dictionary key and value, as <paramref name="options"/> resolves them.
/// </summary>
internal static class JsonGraph
{
    public static IReadOnlyList<Type> Types(JsonSerializerOptions options, params Type[] roots)
    {
        var seen = new HashSet<Type>();
        var pending = new Queue<Type>(roots);
        while (pending.Count > 0)
        {
            Type type = pending.Dequeue();
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (!seen.Add(type) || type == typeof(object))
            {
                continue;
            }

            JsonTypeInfo info = options.GetTypeInfo(type);
            IEnumerable<Type> next = info.Kind switch
            {
                JsonTypeInfoKind.Object => info.Properties.Select(property =>
                    property.PropertyType
                ),
                JsonTypeInfoKind.Enumerable => [info.ElementType],
                JsonTypeInfoKind.Dictionary => [info.KeyType, info.ElementType],
                _ => [],
            };
            foreach (Type child in next)
            {
                pending.Enqueue(child);
            }
        }

        return seen.OrderBy(type => type.FullName, StringComparer.Ordinal).ToList();
    }

    /// <summary>The enums the metadata reaches.</summary>
    public static IReadOnlyList<Type> Enums(JsonSerializerOptions options, params Type[] roots) =>
        Types(options, roots).Where(type => type.IsEnum).ToList();

    /// <summary>The enums of <see cref="Enums"/> that <paramref name="options"/> writes as numbers.</summary>
    public static IReadOnlyList<Type> EnumsAsNumbers(
        JsonSerializerOptions options,
        params Type[] roots
    ) =>
        Enums(options, roots)
            .Where(type =>
            {
                string json = JsonSerializer.Serialize(
                    Enum.GetValues(type).GetValue(0),
                    options.GetTypeInfo(type)
                );
                return json.Length > 0 && json[0] != '"';
            })
            .ToList();
}
