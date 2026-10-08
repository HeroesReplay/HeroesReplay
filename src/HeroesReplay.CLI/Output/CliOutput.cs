using System;
using System.CommandLine;
using System.IO;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Output;

public enum CliOutputFormat
{
    Text,
    Json,
}

/// <summary>
/// The <c>--output text|json</c> contract every agent-facing command shares (#311). Text is the
/// default and is for people. JSON is one <see cref="ICliResult"/> document on stdout: it starts
/// with <c>schemaVersion</c>, <c>ok</c>, and a stable <c>code</c>, and the exit code is 0 exactly
/// when <c>ok</c> is true. Logs and progress go to stderr in JSON mode, so stdout parses. Any
/// other value, such as <c>yaml</c>, is a parse error (exit 1) before anything runs.
/// </summary>
public static class CliOutput
{
    public const string OptionName = "--output";
    public const string Alias = "-o";
    public const string Text = "text";
    public const string Json = "json";

    /// <summary>
    /// The option. <paramref name="json"/> says what the JSON holds. A recursive option also
    /// applies to every subcommand, as on <c>check</c>.
    /// </summary>
    public static Option<string> CreateOption(string json, bool recursive = false)
    {
        var option = new Option<string>(OptionName, Alias)
        {
            Description = "text (default) or json. " + json,
            DefaultValueFactory = _ => Text,
            Recursive = recursive,
        };
        option.AcceptOnlyFromAmong(Text, Json);
        return option;
    }

    public static CliOutputFormat Parse(string value) =>
        string.Equals(value, Json, StringComparison.OrdinalIgnoreCase)
            ? CliOutputFormat.Json
            : CliOutputFormat.Text;

    public static CliOutputFormat Format(ParseResult parseResult, Option<string> option) =>
        Parse(parseResult?.GetValue(option));

    /// <summary>Where the result goes: the invocation's output (a test's writer), else stdout.</summary>
    public static TextWriter Out(ParseResult parseResult) =>
        parseResult?.InvocationConfiguration?.Output ?? Console.Out;

    /// <summary>The invocation's error writer, else stderr.</summary>
    public static TextWriter Error(ParseResult parseResult) =>
        parseResult?.InvocationConfiguration?.Error ?? Console.Error;

    /// <summary>Where logs and progress go: stderr in JSON mode, else <paramref name="text"/>.</summary>
    public static TextWriter Progress(
        CliOutputFormat format,
        ParseResult parseResult,
        TextWriter text
    ) => format == CliOutputFormat.Json ? Error(parseResult) : text;

    /// <summary>Writes <paramref name="result"/> as one JSON document and returns its exit code.</summary>
    public static int WriteJson(ICliResult result, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(result);
        (output ?? Console.Out).WriteLine(CliJson.Serialize(result));
        return ExitCode(result);
    }

    public static int ExitCode(ICliResult result) => result?.Ok == true ? 0 : 1;
}
