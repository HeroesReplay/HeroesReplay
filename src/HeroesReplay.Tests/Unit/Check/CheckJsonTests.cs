using System.CommandLine;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using HeroesReplay.CLI.Commands;
using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Twitch;
using Xunit;

namespace HeroesReplay.Tests.Unit.Check;

/// <summary><c>check --output json</c> (#311): the envelope, the stable codes, and redaction.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class CheckJsonTests
{
    private const string TwitchToken = "k3n9q2w8e7r6t5y4u3i2o1p0a9s8d7";

    [Fact]
    public void OneCheck_IsTheEnvelopeWithItsOwnCode()
    {
        CliResult<CheckDetails> report = CheckCommand.Report([
            new CheckCommand.CheckResult(
                "obs",
                false,
                "No Identify from ws://127.0.0.1:4455.",
                CheckCodes.ObsUnreachable
            ),
        ]);

        using JsonDocument json = JsonDocument.Parse(CliJson.Serialize(report));
        JsonElement root = json.RootElement;
        Assert.Equal(
            ["schemaVersion", "ok", "code", "message", "environment", "details"],
            root.EnumerateObject().Select(property => property.Name)
        );
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("check.obs.unreachable", root.GetProperty("code").GetString());
        JsonElement check = Assert.Single(
            root.GetProperty("details").GetProperty("checks").EnumerateArray()
        );
        Assert.Equal(
            ["name", "ok", "status", "code", "detail"],
            check.EnumerateObject().Select(property => property.Name)
        );
        Assert.Equal("obs", check.GetProperty("name").GetString());
        Assert.Equal("fail", check.GetProperty("status").GetString());
        Assert.Equal(1, CliOutput.ExitCode(report));
    }

    [Fact]
    public void SeveralChecks_TheFirstFailureIsTheCode()
    {
        CliResult<CheckDetails> report = CheckCommand.Report([
            new CheckCommand.CheckResult("config", true, "fine", CheckCodes.ConfigOk),
            new CheckCommand.CheckResult(
                "ffmpeg",
                true,
                CheckCommand.WarningPrefix + "Not the pinned 9.0.2",
                CheckCodes.FfmpegNotPinned
            ),
            new CheckCommand.CheckResult(
                "twitch",
                false,
                "missing",
                CheckCodes.TwitchCredentialsMissing
            ),
            new CheckCommand.CheckResult("obs", false, "closed", CheckCodes.ObsUnreachable),
        ]);

        Assert.False(report.Ok);
        Assert.Equal(CheckCodes.TwitchCredentialsMissing, report.Code);
        Assert.Equal("2 of 4 check(s) failed: twitch, obs.", report.Message);
        Assert.Equal(
            ["ok", "warn", "fail", "fail"],
            report.Details.Checks.Select(check => check.Status)
        );
    }

    [Fact]
    public void AWarning_Passes_AndItsDetailLosesThePrefix()
    {
        CliResult<CheckDetails> report = CheckCommand.Report([
            new CheckCommand.CheckResult("config", true, "fine", CheckCodes.ConfigOk),
            new CheckCommand.CheckResult(
                "ffmpeg",
                true,
                CheckCommand.WarningPrefix + "Not the pinned 9.0.2",
                CheckCodes.FfmpegNotPinned
            ),
        ]);

        Assert.True(report.Ok);
        Assert.Equal(CheckCodes.FfmpegNotPinned, report.Code);
        Assert.Equal("Not the pinned 9.0.2", report.Details.Checks[1].Detail);
        Assert.Equal(0, CliOutput.ExitCode(report));
    }

    [Fact]
    public void EveryCheckPassing_IsCheckOk()
    {
        CliResult<CheckDetails> report = CheckCommand.Report([
            new CheckCommand.CheckResult("config", true, "fine", CheckCodes.ConfigOk),
            new CheckCommand.CheckResult("client", true, "fine", CheckCodes.ClientOk),
        ]);

        Assert.True(report.Ok);
        Assert.Equal(CheckCodes.AllOk, report.Code);
        Assert.Equal("2 check(s) passed.", report.Message);
    }

    [Fact]
    public void Finish_GivesAMissingCodeAndMasksEveryResolvedSecret()
    {
        var redaction = new CliRedaction();
        redaction.Remember(
            new AppSettings
            {
                Twitch = new TwitchSettings { AccessToken = TwitchToken },
                OBS = new OBSSettings { WebSocketPassword = "hunter2-obs" },
            }
        );

        CheckCommand.CheckResult failed = CheckCommand.Finish(
            new CheckCommand.CheckResult(
                "twitch",
                false,
                $"Helix rejected {TwitchToken}; obs said hunter2-obs; url had api_key=abc123 in it."
            ),
            redaction
        );
        CheckCommand.CheckResult passed = CheckCommand.Finish(
            new CheckCommand.CheckResult("twitch-extension", true, "fine"),
            redaction
        );

        Assert.Equal("check.twitch.error", failed.Code);
        Assert.Equal("check.twitch_extension.ok", passed.Code);
        Assert.Equal(
            "Helix rejected [redacted]; obs said [redacted]; url had api_key=[redacted] in it.",
            failed.Detail
        );
    }

    [Fact]
    public void TheJson_NeverCarriesATokenShapedValue()
    {
        var redaction = new CliRedaction();
        redaction.Remember(
            new AppSettings { Twitch = new TwitchSettings { AccessToken = TwitchToken } }
        );
        CliResult<CheckDetails> report = CheckCommand.Report([
            CheckCommand.Finish(
                new CheckCommand.CheckResult(
                    "twitch",
                    false,
                    $"Authorization: Bearer {TwitchToken} and oauth:{TwitchToken}",
                    CheckCodes.TwitchError
                ),
                redaction
            ),
        ]);

        string json = CliJson.Serialize(report);

        Assert.DoesNotContain(TwitchToken, json);
        Assert.Contains("Bearer [redacted]", json);
    }

    [Theory]
    [InlineData(true, null, true, "check.obs.ok")]
    [InlineData(true, null, false, "check.obs.files_invalid")]
    [InlineData(false, ObsSelection.ProfileMismatch, true, "check.obs.profile_mismatch")]
    [InlineData(false, ObsSelection.CollectionMismatch, false, "check.obs.collection_mismatch")]
    [InlineData(false, ObsSelection.Unreadable, true, "check.obs.selection_unreadable")]
    public void ObsCode_TheSelectionComesBeforeTheFiles(
        bool selectionOk,
        string reason,
        bool filesOk,
        string code
    )
    {
        Assert.Equal(
            code,
            CheckCommand.ObsCode(new ObsSelectionResult(selectionOk, reason, "detail"), filesOk)
        );
    }

    [Theory]
    [InlineData(true, true, true, "check.twitch.ok")]
    [InlineData(false, false, false, "check.twitch.predictions_scope_missing")]
    [InlineData(true, false, false, "check.twitch.chat_scope_missing")]
    [InlineData(true, true, false, "check.twitch.redemptions_scope_missing")]
    public void TwitchCode_NamesTheFirstMissingScope(
        bool predictions,
        bool chat,
        bool rewards,
        string code
    )
    {
        Assert.Equal(code, CheckCommand.TwitchCode(predictions, chat, rewards));
    }

    [Fact]
    public void Codes_AreStableNamesWithAnOkAndAnErrorPerTarget()
    {
        Assert.Equal(CheckCodes.All.Count, CheckCodes.All.Distinct().Count());
        Assert.All(
            CheckCodes.All,
            code => Assert.Matches(new Regex("^check(\\.[a-z0-9_]+){1,2}$"), code)
        );
        foreach (CheckTarget target in CheckCommand.Targets)
        {
            Assert.Contains(CheckCodes.For(target.Name, "ok"), CheckCodes.All);
            Assert.Contains(CheckCodes.For(target.Name, "error"), CheckCodes.All);
        }
    }

    [Fact]
    public void Targets_AreTheSubcommands_AndBareCheckRunsTheInAllOnes()
    {
        Command check = new HeroesReplayCommand().Subcommands.Single(c => c.Name == "check");

        Assert.Equal(
            CheckCommand.Targets.Select(target => target.Name),
            check.Subcommands.Select(command => command.Name)
        );
        Assert.Equal(
            CheckCommand
                .Targets.Where(target => target.InAll)
                .Select(target => target.Name)
                .Order(),
            CheckCommand.AllOrder.Order()
        );
    }

    [Fact]
    public void TheOutputOption_AppliesToEveryTarget()
    {
        var root = new HeroesReplayCommand();

        Assert.Empty(root.Parse("check --output json").Errors);
        foreach (CheckTarget target in CheckCommand.Targets)
        {
            Assert.Empty(root.Parse($"check {target.Name} --output json").Errors);
            Assert.Empty(root.Parse($"check {target.Name} -o text").Errors);
            Assert.NotEmpty(root.Parse($"check {target.Name} --output yaml").Errors);
        }
    }
}
