using System.Text.Json.Serialization;
using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.Core.Obs.Inspection;

namespace HeroesReplay.CLI.Commands.Mcp;

/// <summary>
/// Source-generated JSON for every MCP tool's parameters and results (#454). It is chained after
/// the MCP SDK's own resolver in <see cref="McpCommand.ToolJsonOptions"/>, so a tool needs no
/// reflection to build its schema, read its arguments, or write its result.
/// </summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(SpectatorStatusResult))]
[JsonSerializable(typeof(CurrentFocusResult))]
[JsonSerializable(typeof(CheckCommand.CheckResult))]
[JsonSerializable(typeof(ObsInspection))]
[JsonSerializable(typeof(ObsValidation))]
internal sealed partial class McpToolsJsonContext : JsonSerializerContext;
