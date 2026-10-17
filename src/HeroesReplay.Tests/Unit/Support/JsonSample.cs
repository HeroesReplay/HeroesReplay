using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeroesReplay.Tests.Unit.Support;

/// <summary>
/// A value of a JSON file's type with every property set (#454): strings, numbers and dates that
/// are not defaults, the last value of each enum, and one item in each list and dictionary. A
/// string holds characters the encoders escape differently, so a sample also checks the encoder.
/// </summary>
internal static class JsonSample
{
    private const int MaxDepth = 6;

    public static T Of<T>() => (T)Of(typeof(T), "value", 0);

    private static object Of(Type type, string name, int depth)
    {
        Type underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
        {
            return Of(underlying, name, depth);
        }

        if (Scalar(type, name, out object scalar))
        {
            return scalar;
        }

        if (
            type == typeof(object)
            || type == typeof(JsonElement)
            || typeof(JsonNode).IsAssignableFrom(type)
            || type.IsAbstract && !type.IsInterface
            || depth > MaxDepth
        )
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        if (type.IsArray)
        {
            Type element = type.GetElementType();
            var array = Array.CreateInstance(element, 1);
            array.SetValue(Of(element, name, depth + 1), 0);
            return array;
        }

        if (Collection(type, name, depth, out object collection))
        {
            return collection;
        }

        return type.IsInterface ? null : Object(type, depth);
    }

    private static bool Scalar(Type type, string name, out object value)
    {
        value = type switch
        {
            _ when type == typeof(string) => name + " <a&b> 'é' \"q\" C:\\x/y",
            _ when type == typeof(bool) => true,
            _ when type == typeof(byte) => (byte)3,
            _ when type == typeof(short) => (short)5,
            _ when type == typeof(int) => 7,
            _ when type == typeof(long) => 70_000_000_001L,
            _ when type == typeof(uint) => 9u,
            _ when type == typeof(ulong) => 11ul,
            _ when type == typeof(float) => 1.5f,
            _ when type == typeof(double) => 2.25,
            _ when type == typeof(decimal) => 3.75m,
            _ when type == typeof(char) => 'c',
            _ when type == typeof(Guid) => Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
            _ when type == typeof(TimeSpan) => TimeSpan.FromSeconds(83.5),
            _ when type == typeof(DateTime) => new DateTime(
                2026,
                10,
                17,
                9,
                30,
                15,
                DateTimeKind.Utc
            ),
            _ when type == typeof(DateTimeOffset) => new DateTimeOffset(
                2026,
                10,
                17,
                9,
                30,
                15,
                TimeSpan.FromHours(1)
            ),
            _ when type == typeof(DateOnly) => new DateOnly(2026, 10, 17),
            _ when type == typeof(TimeOnly) => new TimeOnly(9, 30, 15),
            _ when type == typeof(Uri) => new Uri(
                "https://example.com/" + Uri.EscapeDataString(name)
            ),
            _ when type == typeof(Version) => new Version(1, 2, 3),
            _ when type.IsEnum => Last(type),
            _ => null,
        };
        return value != null;
    }

    private static object Last(Type type)
    {
        Array values = Enum.GetValues(type);
        return values.GetValue(values.Length - 1);
    }

    private static bool Collection(Type type, string name, int depth, out object value)
    {
        value = null;
        Type dictionary = Generic(type, typeof(IDictionary<,>), typeof(IReadOnlyDictionary<,>));
        if (dictionary != null)
        {
            Type[] arguments = dictionary.GetGenericArguments();
            Type made = type.IsInterface ? typeof(Dictionary<,>).MakeGenericType(arguments) : type;
            var map = (IDictionary)Activator.CreateInstance(made);
            map.Add(Of(arguments[0], name + "Key", depth + 1), Of(arguments[1], name, depth + 1));
            value = map;
            return true;
        }

        Type enumerable = Generic(type, typeof(IEnumerable<>));
        if (enumerable == null || type == typeof(string))
        {
            return false;
        }

        Type item = enumerable.GetGenericArguments()[0];
        Type concrete = type.IsInterface
            ? (
                Generic(type, typeof(ISet<>)) != null
                    ? typeof(HashSet<>).MakeGenericType(item)
                    : typeof(List<>).MakeGenericType(item)
            )
            : type;
        object list = Activator.CreateInstance(concrete);
        concrete.GetMethod("Add", [item]).Invoke(list, [Of(item, name, depth + 1)]);
        value = list;
        return true;
    }

    private static Type Generic(Type type, params Type[] definitions) =>
        new[] { type }
            .Concat(type.GetInterfaces())
            .FirstOrDefault(candidate =>
                candidate.IsGenericType
                && definitions.Contains(candidate.GetGenericTypeDefinition())
            );

    private static object Object(Type type, int depth)
    {
        ConstructorInfo constructor =
            type.GetConstructor(Type.EmptyTypes)
            ?? type.GetConstructors()
                .OrderByDescending(candidate => candidate.GetParameters().Length)
                .FirstOrDefault();
        object instance =
            constructor == null
                ? Activator.CreateInstance(type)
                : constructor.Invoke(
                    constructor
                        .GetParameters()
                        .Select(parameter =>
                            Of(parameter.ParameterType, Name(parameter.Name), depth + 1)
                        )
                        .ToArray()
                );

        foreach (
            PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        )
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (property.SetMethod?.IsPublic == true)
            {
                property.SetValue(instance, Of(property.PropertyType, property.Name, depth + 1));
            }
            else if (
                property.GetValue(instance) is IList { IsReadOnly: false, IsFixedSize: false } list
                && list.Count == 0
                && Generic(property.PropertyType, typeof(IEnumerable<>)) is Type items
            )
            {
                list.Add(Of(items.GetGenericArguments()[0], property.Name, depth + 1));
            }
        }

        return instance;
    }

    private static string Name(string parameter) =>
        string.IsNullOrEmpty(parameter)
            ? parameter
            : char.ToUpperInvariant(parameter[0]) + parameter[1..];
}
