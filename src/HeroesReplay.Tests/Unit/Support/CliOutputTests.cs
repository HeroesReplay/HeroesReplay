using System.CommandLine;
using System.IO;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Twitch;
using Xunit;

namespace HeroesReplay.Tests.Unit.Support;

/// <summary>The <c>--output</c> option and the redaction every agent-facing command shares (#311).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class CliOutputTests
{
    [Fact]
    public void TheOption_IsTextByDefault_AndAcceptsOnlyTextOrJson()
    {
        var command = new RootCommand();
        Option<string> output = CliOutput.CreateOption("JSON: test.");
        command.Options.Add(output);

        Assert.Equal(CliOutputFormat.Text, CliOutput.Format(command.Parse(""), output));
        Assert.Equal(CliOutputFormat.Json, CliOutput.Format(command.Parse("-o json"), output));
        Assert.Equal(
            CliOutputFormat.Text,
            CliOutput.Format(command.Parse("--output text"), output)
        );
        Assert.NotEmpty(command.Parse("--output yaml").Errors);
        Assert.StartsWith("text (default) or json. JSON: test.", output.Description);
    }

    [Fact]
    public void ARecursiveOption_ReachesTheSubcommands()
    {
        var root = new RootCommand();
        Option<string> output = CliOutput.CreateOption("JSON.", recursive: true);
        root.Options.Add(output);
        root.Subcommands.Add(new Command("child"));

        ParseResult parse = root.Parse("child --output json");

        Assert.Empty(parse.Errors);
        Assert.Equal(CliOutputFormat.Json, CliOutput.Format(parse, output));
    }

    [Fact]
    public void WriteJson_PrintsOneDocument_AndTheExitCodeFollowsOk()
    {
        var output = new StringWriter();

        int failed = CliOutput.WriteJson(
            new CliResult<object> { Ok = false, Code = "x.failed" },
            output
        );
        int passed = CliOutput.WriteJson(
            new CliResult<object> { Ok = true, Code = "x.ok" },
            new StringWriter()
        );

        Assert.Equal(1, failed);
        Assert.Equal(0, passed);
        Assert.StartsWith("{", output.ToString());
        Assert.Contains("\"code\": \"x.failed\"", output.ToString());
    }

    [Fact]
    public void Redaction_MasksEveryResolvedSecret()
    {
        var redaction = new CliRedaction();
        redaction.Remember(
            new AppSettings
            {
                HeroesProfileApi = new HeroesProfileApiSettings { ApiKey = "hp-key-0123456789" },
                Twitch = new TwitchSettings
                {
                    AccessToken = "twitchtoken0123456789abcdef",
                    ClientId = "clientid0123456789",
                },
                OBS = new OBSSettings { WebSocketPassword = "obs-password-1" },
            }
        );

        string text = redaction.Redact(
            "key hp-key-0123456789, token twitchtoken0123456789abcdef, client clientid0123456789, obs obs-password-1."
        );

        Assert.Equal("key [redacted], token [redacted], client [redacted], obs [redacted].", text);
    }

    [Theory]
    [InlineData("GET /x?access_token=abc123&y=1", "GET /x?access_token=[redacted]&y=1")]
    [InlineData("Authorization: Bearer abc.def-ghi", "Authorization: Bearer [redacted]")]
    [InlineData("PASS oauth:abcdef123", "PASS oauth: [redacted]")]
    [InlineData("Check the v1 Bearer key.", "Check the v1 Bearer key.")]
    [InlineData("OBS password: set (12 chars)", "OBS password: set (12 chars)")]
    [InlineData(
        "PUT https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&key=unit-test-yt-key&upload_id=abc",
        "PUT https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&key=[redacted]&upload_id=abc"
    )]
    public void Redaction_MasksTokenShapedValues(string text, string expected)
    {
        Assert.Equal(expected, new CliRedaction().Redact(text));
    }

    [Fact]
    public void Redaction_MasksAGoogleApiKeyItWasNotTold_AndTheYouTubeKeyItWas()
    {
        // Built at run time so the source holds nothing shaped like a real key.
        string shaped = "AIza" + new string('Q', 35);
        var redaction = new CliRedaction();
        redaction.Remember(
            new AppSettings
            {
                YouTube = new HeroesReplay.Core.YouTube.YouTubeSettings
                {
                    ApiKey = "unit-test-yt-key",
                },
            }
        );

        string text = redaction.Redact(
            "{\"key\": \"" + shaped + "\", \"session\": \"x?upload_id=abc&key%3Dunit-test-yt-key\"}"
        );

        Assert.DoesNotContain(shaped, text);
        Assert.DoesNotContain("unit-test-yt-key", text);
        Assert.Contains("upload_id=abc", text);
    }

    [Fact]
    public void Redaction_IgnoresReferencesAndShortValues()
    {
        var redaction = new CliRedaction();
        redaction.Remember("op://Heroes Replay/Heroes Profile API Key/password");
        redaction.Remember("true");
        redaction.Remember((string)null);

        Assert.Equal(
            "op://Heroes Replay/Heroes Profile API Key/password is true",
            redaction.Redact("op://Heroes Replay/Heroes Profile API Key/password is true")
        );
    }
}
