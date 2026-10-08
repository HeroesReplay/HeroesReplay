using System;
using System.IO;
using System.Text.Json;
using HeroesReplay.CLI.Commands.Obs;
using HeroesReplay.Core.Obs.Inspection;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Inspection;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsLiveCommandsTests : IDisposable
{
    private readonly string data = Path.Combine(
        Path.GetTempPath(),
        "hr-obs-cli-" + Path.GetRandomFileName()
    );

    public ObsLiveCommandsTests()
    {
        Directory.CreateDirectory(data);
    }

    public void Dispose()
    {
        Directory.Delete(data, recursive: true);
    }

    [Fact]
    public void Validate_Json_IsTheToolEnvelopeAndExitsZeroWithoutErrors()
    {
        var output = new StringWriter();

        int exit = ObsLiveCommands.Validate(
            FakeObs.Installed(data),
            () => FakeObs.InspectionSettings(data),
            json: true,
            output
        );

        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal(0, exit);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(0, document.RootElement.GetProperty("errors").GetInt32());
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("findings").ValueKind);
    }

    [Fact]
    public void Validate_AnErrorFinding_ExitsOneAndPrintsItsCode()
    {
        FakeObs obs = FakeObs.Installed(data);
        obs.Video["baseWidth"] = 1280;
        obs.Video["baseHeight"] = 720;
        var output = new StringWriter();

        int exit = ObsLiveCommands.Validate(
            obs,
            () => FakeObs.InspectionSettings(data),
            json: false,
            output
        );

        Assert.Equal(1, exit);
        Assert.Contains("error obs.canvas_mismatch 1280x720:", output.ToString());
    }

    [Fact]
    public void Inspect_Text_NamesTheCanvasAndRecordingFormatButNeverTheKey()
    {
        var output = new StringWriter();

        int exit = ObsLiveCommands.Inspect(
            FakeObs.Installed(data),
            () => FakeObs.InspectionSettings(data),
            json: false,
            output
        );

        string text = output.ToString();
        Assert.Equal(0, exit);
        Assert.Contains("Canvas 1920x1080, output 1280x720", text);
        Assert.Contains("Simple output: records mp4 with qsv_h264", text);
        Assert.Contains("Stream service: Twitch, key set.", text);
        Assert.Contains(
            "Bitrate: stream 6000 kbps CBR, recording Stream quality, 6000 kbps CBR.",
            text
        );
        Assert.Contains(@"Record directory: C:\heroesreplay\Data\Contexts\65820711.", text);
        Assert.DoesNotContain(FakeObs.StreamKey, text);
    }

    [Fact]
    public void Inspect_Json_NeverHasTheKey()
    {
        var output = new StringWriter();

        ObsLiveCommands.Inspect(
            FakeObs.Installed(data),
            () => FakeObs.InspectionSettings(data),
            json: true,
            output
        );

        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal(
            "mp4",
            document.RootElement.GetProperty("profile").GetProperty("recordingFormat").GetString()
        );
        Assert.Equal(
            6000,
            document.RootElement.GetProperty("profile").GetProperty("streamBitrateKbps").GetInt64()
        );
        Assert.Equal(
            @"C:\heroesreplay\Data\Contexts\65820711",
            document.RootElement.GetProperty("recordDirectory").GetString()
        );
        Assert.DoesNotContain(FakeObs.StreamKey, output.ToString());
    }

    [Fact]
    public void Unreachable_ExitsOneWithTheStableCode()
    {
        var output = new StringWriter();

        int validate = ObsLiveCommands.Validate(
            new Unreachable(),
            () => FakeObs.InspectionSettings(data),
            json: true,
            output
        );
        int inspect = ObsLiveCommands.Inspect(
            new Unreachable(),
            () => FakeObs.InspectionSettings(data),
            json: false,
            output
        );

        Assert.Equal(1, validate);
        Assert.Equal(1, inspect);
        Assert.Contains("\"code\": \"obs.unreachable\"", output.ToString());
        Assert.Contains("OBS was not read (obs.unreachable)", output.ToString());
    }

    private sealed class Unreachable : IObsReadSessionFactory
    {
        public IObsReadSession Open(string endpoint, string password) =>
            throw new ObsUnavailableException(
                ObsUnavailableException.Unreachable,
                "OBS is not running."
            );
    }
}
