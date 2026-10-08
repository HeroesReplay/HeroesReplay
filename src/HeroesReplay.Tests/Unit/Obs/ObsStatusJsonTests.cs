using System.IO;
using System.Text.Json;
using HeroesReplay.CLI.Commands.Obs;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

/// <summary><c>obs status --output json</c> (#311). It never connects to OBS.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsStatusJsonTests
{
    private const string ArmFile = @"C:\Users\x\AppData\Local\HeroesReplay\stream-armed";

    [Theory]
    [InlineData(true, true, "obs.ingest_ready", true, null)]
    [InlineData(true, false, "obs.stream_not_armed", false, "obs.stream_not_armed")]
    [InlineData(false, true, "obs.streaming_disabled", false, null)]
    [InlineData(false, false, "obs.streaming_disabled", false, null)]
    public void TheCodeSaysWhetherIngestMayStart(
        bool streaming,
        bool armed,
        string code,
        bool mayStart,
        string blockedBy
    )
    {
        CliResult<ObsStatusDetails> status = ObsCommand.ReadStatus(
            armed,
            ArmFile,
            () => new OBSSettings { StreamingEnabled = streaming, ProfileName = "HeroesReplay" }
        );

        Assert.True(status.Ok);
        Assert.Equal(code, status.Code);
        Assert.Equal(mayStart, status.Details.StreamArm.MayStart);
        Assert.Equal(blockedBy, status.Details.StreamArm.BlockedBy);
        Assert.Equal(ArmFile, status.Details.StreamArm.ArmFile);
        Assert.Equal("HeroesReplay", status.Details.Profile);
        Assert.Equal(0, CliOutput.ExitCode(status));
    }

    [Fact]
    public void UnreadableSettings_ExitOne()
    {
        CliResult<ObsStatusDetails> status = ObsCommand.ReadStatus(
            false,
            ArmFile,
            () => throw new InvalidDataException("bad json")
        );

        Assert.False(status.Ok);
        Assert.Equal("obs.settings_unreadable", status.Code);
        Assert.Contains("bad json", status.Message);
        Assert.Equal(1, CliOutput.ExitCode(status));
    }

    [Fact]
    public void TheJson_IsTheSharedEnvelope()
    {
        var output = new StringWriter();

        CliOutput.WriteJson(
            ObsCommand.ReadStatus(true, ArmFile, () => new OBSSettings { StreamingEnabled = true }),
            output
        );

        using JsonDocument json = JsonDocument.Parse(output.ToString());
        JsonElement details = json.RootElement.GetProperty("details");
        Assert.Equal("obs.ingest_ready", json.RootElement.GetProperty("code").GetString());
        Assert.True(details.GetProperty("streamArm").GetProperty("armed").GetBoolean());
        Assert.Equal("HeroesReplay", details.GetProperty("sceneCollection").GetString());
        Assert.False(details.TryGetProperty("streamKey", out _));
    }
}
