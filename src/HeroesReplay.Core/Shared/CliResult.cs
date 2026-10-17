using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.Core.Shared;

/// <summary>
/// The fields every <c>--output json</c> result starts with (#311): <see cref="SchemaVersion"/>,
/// <see cref="Ok"/> (exit 0 exactly when true), and a stable <see cref="Code"/>. Every
/// <see cref="CliResult{TDetails}"/> implements it, and so do the results that came before the
/// envelope and keep their own fields after these three: <c>services status</c> and
/// <c>ensure</c>, <c>obs inspect</c>, <c>validate</c>, <c>bundle</c>, <c>plan</c>, <c>apply</c>,
/// <c>backup</c> and <c>restore</c>, and <c>config effective</c>.
/// </summary>
public interface ICliResult
{
    int SchemaVersion { get; }
    bool Ok { get; }
    string Code { get; }
}

/// <summary>
/// The shared JSON envelope for agent-facing commands: <c>{ schemaVersion, ok, code, message,
/// environment, details }</c>. <see cref="Message"/> is one line for a person: what happened
/// and what to change. <see cref="Environment"/> is <c>HEROES_REPLAY_ENV</c>, null when it is
/// not set. <see cref="Details"/> is the command's own payload, null when the command could not
/// read anything. No field carries a secret value: a secret shows as present or missing.
/// </summary>
public sealed record CliResult<TDetails> : ICliResult
{
    public int SchemaVersion { get; init; } = CliJson.SchemaVersion;
    public bool Ok { get; init; }
    public string Code { get; init; }
    public string Message { get; init; }
    public string Environment { get; init; }
    public TDetails Details { get; init; }
}

/// <summary>
/// How every <c>--output json</c> result is written: camelCase names, indented, enums as
/// camelCase strings, and no HTML escaping, so paths and messages read as they are.
/// </summary>
public static class CliJson
{
    /// <summary>The current envelope version. A field is only ever added within a version.</summary>
    public const int SchemaVersion = 1;

    /// <summary>The environment variable whose value is <see cref="CliResult{TDetails}.Environment"/>.</summary>
    public const string EnvironmentVariable = "HEROES_REPLAY_ENV";

    /// <summary>Core's results (<see cref="CliJsonContext"/>) with the envelope's options.</summary>
    internal static readonly CliJsonContext Json = new(NewOptions());

    /// <summary>The options of every result Core writes.</summary>
    public static JsonSerializerOptions Options => Json.Options;

    /// <summary>
    /// The envelope's options over <paramref name="resolver"/>'s results and Core's: the CLI's
    /// own results, with <paramref name="converters"/> for the enums only they hold.
    /// </summary>
    public static JsonSerializerOptions CreateOptions(
        IJsonTypeInfoResolver resolver,
        params JsonConverter[] converters
    )
    {
        JsonSerializerOptions options = NewOptions();
        options.TypeInfoResolver = JsonTypeInfoResolver.Combine(resolver, CliJsonContext.Default);
        foreach (JsonConverter converter in converters)
        {
            options.Converters.Add(converter);
        }

        return options;
    }

    /// <summary>The result as its runtime type, so an <see cref="ICliResult"/> keeps every field.</summary>
    public static string Serialize(ICliResult result) => Serialize(result, Options);

    /// <summary>
    /// <see cref="Serialize(ICliResult)"/> with <paramref name="options"/> from
    /// <see cref="CreateOptions"/>. A result type no resolver holds throws <see cref="NotSupportedException"/>.
    /// </summary>
    public static string Serialize(ICliResult result, JsonSerializerOptions options) =>
        result == null
            ? "null"
            : JsonSerializer.Serialize(result, options.GetTypeInfo(result.GetType()));

    public static string CurrentEnvironment() =>
        System.Environment.GetEnvironmentVariable(EnvironmentVariable);

    /// <summary>
    /// camelCase names, indented, no HTML escaping, and the enums Core's results hold as camelCase
    /// strings. Source generation needs each enum's converter by name (#454).
    /// </summary>
    private static JsonSerializerOptions NewOptions() =>
        new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters =
            {
                new JsonStringEnumConverter<ObsDiffApply>(JsonNamingPolicy.CamelCase),
                new JsonStringEnumConverter<ObsDiffEntity>(JsonNamingPolicy.CamelCase),
                new JsonStringEnumConverter<ObsDiffKind>(JsonNamingPolicy.CamelCase),
                new JsonStringEnumConverter<ServiceRoleState>(JsonNamingPolicy.CamelCase),
            },
        };
}
