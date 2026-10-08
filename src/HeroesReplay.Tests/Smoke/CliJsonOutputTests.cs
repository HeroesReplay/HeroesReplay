using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.CLI.Commands;
using Xunit;

namespace HeroesReplay.Tests.Smoke;

/// <summary>
/// One <c>--output text|json</c> contract (#311): every converted command parses <c>json</c>
/// and <c>text</c> and refuses anything else before it runs, and the commands that read only
/// local state print one JSON document on stdout with the shared envelope.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Smoke)]
public class CliJsonOutputTests
{
    public static TheoryData<string> Converted() =>
        new(
            "check",
            "check config",
            "check heroesprofile",
            "check obs",
            "check twitch",
            "check client",
            "check connectivity",
            "check timer",
            "check twitch-extension",
            "check battlenet",
            "check ffmpeg",
            "obs status",
            "obs inspect",
            "obs validate",
            "client status",
            "deps install",
            "services status"
        );

    [Theory]
    [MemberData(nameof(Converted))]
    public void JsonAndText_Parse_AndYamlIsAParseError(string command)
    {
        var root = new HeroesReplayCommand();

        Assert.Empty(root.Parse(command + " --output json").Errors);
        Assert.Empty(root.Parse(command + " -o text").Errors);
        ParseResult yaml = root.Parse(command + " --output yaml");
        Assert.NotEmpty(yaml.Errors);
        Assert.NotSame(yaml.CommandResult.Command.Action, yaml.Action);
    }

    [Theory]
    [MemberData(nameof(Converted))]
    public async Task Yaml_ExitsOneWithoutRunning(string command)
    {
        (int code, string output, string error) = await InvokeAsync(command + " --output yaml");

        Assert.Equal(1, code);
        // Help and the parse error, never a JSON result.
        Assert.False(output.TrimStart().StartsWith('{'), output);
        Assert.Contains("yaml", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObsStatus_PrintsTheEnvelope()
    {
        (int code, string output, _) = await InvokeAsync("obs status --output json");

        JsonElement root = Envelope(output);
        Assert.Equal(0, code);
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Contains(
            root.GetProperty("code").GetString(),
            new[] { "obs.ingest_ready", "obs.stream_not_armed", "obs.streaming_disabled" }
        );
        Assert.Equal(
            JsonValueKind.Object,
            root.GetProperty("details").GetProperty("streamArm").ValueKind
        );
    }

    [Fact]
    public async Task CheckFfmpeg_PrintsOneCheckWithAStableCode()
    {
        (int code, string output, _) = await InvokeAsync("check ffmpeg --output json");

        JsonElement root = Envelope(output);
        JsonElement check = Assert.Single(
            root.GetProperty("details").GetProperty("checks").EnumerateArray()
        );
        Assert.Equal("ffmpeg", check.GetProperty("name").GetString());
        Assert.StartsWith("check.ffmpeg.", check.GetProperty("code").GetString());
        Assert.Equal(check.GetProperty("code").GetString(), root.GetProperty("code").GetString());
        Assert.Equal(root.GetProperty("ok").GetBoolean() ? 0 : 1, code);
        Assert.DoesNotContain("[OK]", output, StringComparison.Ordinal);
    }

    /// <summary>The whole of stdout is one JSON document that starts with the shared fields.</summary>
    private static JsonElement Envelope(string output)
    {
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement.Clone();
        Assert.Equal(
            ["schemaVersion", "ok", "code", "message", "environment", "details"],
            root.EnumerateObject().Select(property => property.Name)
        );
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        return root;
    }

    private static async Task<(int Code, string Output, string Error)> InvokeAsync(string line)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = await new CommandLineService().InvokeAsync(
            line.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            new InvocationConfiguration { Output = output, Error = error }
        );
        return (code, output.ToString(), error.ToString());
    }
}
