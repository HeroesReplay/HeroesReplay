using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Shared;

/// <summary>
/// The fields every <c>--output json</c> result starts with (#311): <see cref="SchemaVersion"/>,
/// <see cref="Ok"/> (exit 0 exactly when true), and a stable <see cref="Code"/>. Every
/// <see cref="CliResult{TDetails}"/> implements it, and so do the results that came before the
/// envelope and keep their own fields after these three: <c>services status</c> and
/// <c>ensure</c>, <c>obs inspect</c>, <c>validate</c>, <c>bundle</c>, <c>plan</c>,
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

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>The result as its runtime type, so an <see cref="ICliResult"/> keeps every field.</summary>
    public static string Serialize(ICliResult result) =>
        JsonSerializer.Serialize(result, result?.GetType() ?? typeof(object), Options);

    public static string CurrentEnvironment() =>
        System.Environment.GetEnvironmentVariable(EnvironmentVariable);
}
