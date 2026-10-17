using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.CLI.Commands.Deps;
using HeroesReplay.CLI.Commands.Obs;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Shared;

/// <summary>Every <c>--output json</c> result keeps its bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class CliJsonTests
{
    /// <summary><see cref="CliJson"/>'s options before #454.</summary>
    private static readonly JsonSerializerOptions Before = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public void CoreResults_KeepTheirBytes()
    {
        Same<EffectiveConfiguration>(CliJson.Options);
        Same<ObsApplyResult>(CliJson.Options);
        Same<ObsBackupResult>(CliJson.Options);
        Same<ObsCollectionPlanResult>(CliJson.Options);
        Same<ObsInspection>(CliJson.Options);
        Same<ObsValidation>(CliJson.Options);
        Same<ServiceEnsureReport>(CliJson.Options);
        Same<ServiceStatusReport>(CliJson.Options);
        Same<CliResult<ClientStatusResult>>(CliJson.Options);
        Same<CliResult<FocusCoverageReport>>(CliJson.Options);
    }

    [Fact]
    public void CliResults_KeepTheirBytes()
    {
        Same<CliResult<CheckDetails>>(CliOutput.JsonOptions);
        Same<CliResult<DepsInstallDetails>>(CliOutput.JsonOptions);
        Same<CliResult<ObsStatusDetails>>(CliOutput.JsonOptions);
        Same<ObsBundleReport>(CliOutput.JsonOptions);
        Same<ServiceStatusReport>(CliOutput.JsonOptions);
    }

    [Fact]
    public void EveryEnumInAResult_IsACamelCaseString()
    {
        Assert.Empty(
            JsonGraph.EnumsAsNumbers(
                CliOutput.JsonOptions,
                typeof(EffectiveConfiguration),
                typeof(ObsApplyResult),
                typeof(ObsBackupResult),
                typeof(ObsCollectionPlanResult),
                typeof(ObsInspection),
                typeof(ObsValidation),
                typeof(ServiceEnsureReport),
                typeof(ServiceStatusReport),
                typeof(CliResult<ClientStatusResult>),
                typeof(CliResult<FocusCoverageReport>),
                typeof(CliResult<CheckDetails>),
                typeof(CliResult<DepsInstallDetails>),
                typeof(CliResult<ObsStatusDetails>),
                typeof(ObsBundleReport)
            )
        );
    }

    private static void Same<T>(JsonSerializerOptions after) =>
        JsonParity.AssertSameWhole(Before, (JsonTypeInfo<T>)after.GetTypeInfo(typeof(T)));
}
