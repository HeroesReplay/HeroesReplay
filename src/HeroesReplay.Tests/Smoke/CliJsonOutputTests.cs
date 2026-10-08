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
            "obs bundle",
            "obs plan",
            "obs backup",
            "obs backup --list",
            "obs restore scenes-HeroesReplay.json.20261008T140000000Z.bak",
            "config effective",
            "client status",
            "deps install",
            "services status",
            "services ensure"
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

    [Fact]
    public async Task ConfigEffective_PrintsOneDocument_WithSecretsRedacted()
    {
        string install = Path.Combine(
            Path.GetTempPath(),
            "hr-config-json-" + Path.GetRandomFileName()
        );
        Directory.CreateDirectory(install);
        const string token = "smoke-access-token-0123456789";
        File.WriteAllText(
            Path.Combine(install, "appsettings.json"),
            "{ \"OBS\": { \"ProfileName\": \"HeroesReplay\" }, \"Twitch\": { \"AccessToken\": \""
                + token
                + "\" } }"
        );
        try
        {
            (int code, string output, _) = await InvokeAsync(
                "config",
                "effective",
                "--install",
                install,
                "--section",
                "OBS",
                "--output",
                "json"
            );

            JsonElement root = OwnFields(output, code);
            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("code").ValueKind);
            Assert.Contains(
                root.GetProperty("settings").EnumerateArray(),
                setting =>
                    setting.GetProperty("key").GetString() == "OBS:ProfileName"
                    && setting.GetProperty("value").GetString() == "HeroesReplay"
            );

            (int all, string everything, _) = await InvokeAsync(
                "config",
                "effective",
                "--install",
                install,
                "-o",
                "json"
            );
            Assert.Equal(0, all);
            Assert.DoesNotContain(token, everything, StringComparison.Ordinal);
            Assert.Contains("(set)", everything, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(install, recursive: true);
        }
    }

    [Fact]
    public async Task ConfigEffective_AnUnknownSection_IsNotOk()
    {
        string install = Path.Combine(
            Path.GetTempPath(),
            "hr-config-json-" + Path.GetRandomFileName()
        );
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "appsettings.json"), "{ \"OBS\": {} }");
        try
        {
            (int code, string output, _) = await InvokeAsync(
                "config",
                "effective",
                "--install",
                install,
                "--section",
                "NoSuchSection",
                "--output",
                "json"
            );

            JsonElement root = OwnFields(output, code);
            Assert.Equal(1, code);
            Assert.Equal("config.section_not_found", root.GetProperty("code").GetString());
        }
        finally
        {
            Directory.Delete(install, recursive: true);
        }
    }

    /// <summary>Read-only: it lists the backups and copies nothing.</summary>
    [Fact]
    public async Task ObsBackupList_PrintsOneDocument()
    {
        (int code, string output, _) = await InvokeAsync("obs backup --list --output json");

        JsonElement root = OwnFields(output, code);
        Assert.Contains(
            root.GetProperty("code").GetString(),
            new[] { "obs.backups_listed", "obs.settings_unreadable" }
        );
        Assert.Equal(JsonValueKind.Array, root.GetProperty("backups").ValueKind);
    }

    /// <summary>Read-only: files only, and the update's dry run works on copies in a temp folder.</summary>
    [Fact]
    public async Task ObsPlan_PrintsOneDocument()
    {
        (int code, string output, _) = await InvokeAsync("obs plan --output json");

        JsonElement root = OwnFields(output, code);
        Assert.StartsWith("obs.", root.GetProperty("code").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("differences").ValueKind);
    }

    /// <summary>
    /// A result that came before the shared envelope: the whole of stdout is one JSON document
    /// that starts with <c>schemaVersion</c>, <c>ok</c>, <c>code</c>, and <c>message</c>, and the
    /// exit code is 0 exactly when <c>ok</c>.
    /// </summary>
    private static JsonElement OwnFields(string output, int code)
    {
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement.Clone();
        Assert.Equal(
            ["schemaVersion", "ok", "code", "message"],
            root.EnumerateObject().Take(4).Select(property => property.Name)
        );
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(root.GetProperty("ok").GetBoolean() ? 0 : 1, code);
        return root;
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

    private static Task<(int Code, string Output, string Error)> InvokeAsync(string line) =>
        InvokeAsync(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static async Task<(int Code, string Output, string Error)> InvokeAsync(
        params string[] args
    )
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = await new CommandLineService().InvokeAsync(
            args,
            new InvocationConfiguration { Output = output, Error = error }
        );
        return (code, output.ToString(), error.ToString());
    }
}
