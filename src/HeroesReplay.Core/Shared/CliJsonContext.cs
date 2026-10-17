using System.Text.Json.Serialization;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.Core.Shared;

/// <summary>
/// Source-generated JSON for every <c>--output json</c> result Core owns (#454), so
/// <see cref="CliJson.Serialize(ICliResult)"/> finds each runtime type without reflection. A new
/// <see cref="ICliResult"/> or <see cref="CliResult{TDetails}"/> in Core adds a line here; the CLI
/// registers its own results in its <c>CliOutputJsonContext</c>.
/// </summary>
[JsonSerializable(typeof(EffectiveConfiguration))]
[JsonSerializable(typeof(ObsApplyResult))]
[JsonSerializable(typeof(ObsBackupResult))]
[JsonSerializable(typeof(ObsCollectionPlanResult))]
[JsonSerializable(typeof(ObsInspection))]
[JsonSerializable(typeof(ObsValidation))]
[JsonSerializable(typeof(ServiceEnsureReport))]
[JsonSerializable(typeof(ServiceStatusReport))]
[JsonSerializable(typeof(CliResult<ClientStatusResult>))]
[JsonSerializable(typeof(CliResult<FocusCoverageReport>))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
