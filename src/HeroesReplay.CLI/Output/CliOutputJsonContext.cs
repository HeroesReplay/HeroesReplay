using System.Text.Json.Serialization;
using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.CLI.Commands.Deps;
using HeroesReplay.CLI.Commands.Obs;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Output;

/// <summary>
/// Source-generated JSON for the <c>--output json</c> results the CLI owns (#454). Core's results
/// are in Core's <c>CliJsonContext</c>; <see cref="CliOutput.JsonOptions"/> reads both.
/// </summary>
[JsonSerializable(typeof(CliResult<CheckDetails>))]
[JsonSerializable(typeof(CliResult<DepsInstallDetails>))]
[JsonSerializable(typeof(CliResult<ObsStatusDetails>))]
[JsonSerializable(typeof(ObsBundleReport))]
internal sealed partial class CliOutputJsonContext : JsonSerializerContext;
