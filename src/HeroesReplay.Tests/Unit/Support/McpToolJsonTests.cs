using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.CLI.Commands.Mcp;
using HeroesReplay.Core.Obs.Inspection;
using Xunit;

namespace HeroesReplay.Tests.Unit.Support;

/// <summary>The MCP tools' results keep their JSON on source-generated metadata (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class McpToolJsonTests
{
    /// <summary>The MCP SDK's default options while reflection was on, before #454.</summary>
    private static readonly JsonSerializerOptions Before = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void ToolResults_KeepTheirJson()
    {
        Same<SpectatorStatusResult>();
        Same<CurrentFocusResult>();
        Same<CheckCommand.CheckResult>();
        Same<ObsInspection>();
        Same<ObsValidation>();
    }

    [Fact]
    public void ToolResults_HoldNoEnumThatWouldTurnIntoANumber() =>
        Assert.Empty(
            JsonGraph.EnumsAsNumbers(
                McpCommand.ToolJsonOptions,
                typeof(SpectatorStatusResult),
                typeof(CurrentFocusResult),
                typeof(CheckCommand.CheckResult),
                typeof(ObsInspection),
                typeof(ObsValidation)
            )
        );

    private static void Same<T>() =>
        JsonParity.AssertSameWhole(
            Before,
            (JsonTypeInfo<T>)McpCommand.ToolJsonOptions.GetTypeInfo(typeof(T))
        );
}
