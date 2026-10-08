using System;
using System.Linq;
using System.Text.Json;
using HeroesReplay.CLI.Commands.Obs;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.Shared;

/// <summary>
/// One JSON output contract (#311). The new envelope has a fixed set of fields, and the three
/// results that came first keep exactly the fields they had, so a script that reads them keeps
/// working.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class CliResultTests
{
    [Fact]
    public void TheEnvelope_IsSchemaVersionOkCodeMessageEnvironmentDetails()
    {
        var result = new CliResult<string[]>
        {
            Ok = true,
            Code = "obs.ingest_ready",
            Message = "Twitch ingest: may start.",
            Environment = "dev",
            Details = ["a"],
        };

        Assert.Equal(
            ["schemaVersion", "ok", "code", "message", "environment", "details"],
            Names(CliJson.Serialize(result))
        );
        Assert.Equal(CliJson.SchemaVersion, result.SchemaVersion);
    }

    [Fact]
    public void ServicesStatus_KeepsItsFields()
    {
        var report = new ServiceStatusReport
        {
            Ok = true,
            Code = "service.ready",
            CheckedAt = DateTimeOffset.UnixEpoch,
        };

        Assert.IsAssignableFrom<ICliResult>(report);
        Assert.Equal(
            [
                "schemaVersion",
                "ok",
                "code",
                "message",
                "environment",
                "checkedAt",
                "stopRequested",
                "roles",
                "spectator",
                "supervisor",
                "machine",
            ],
            Names(report.ToJson())
        );
        Assert.Equal(report.ToJson(), CliJson.Serialize(report));
        Assert.Equal("service.ready", ServiceStatusReport.FromJson(report.ToJson()).Code);
    }

    [Fact]
    public void ObsInspect_KeepsItsFields()
    {
        ObsInspection inspection = ObsInspector.Unavailable(
            null,
            ObsUnavailableException.Unreachable,
            "OBS is not running."
        );

        Assert.IsAssignableFrom<ICliResult>(inspection);
        Assert.Equal(
            [
                "schemaVersion",
                "ok",
                "code",
                "error",
                "endpoint",
                "version",
                "selection",
                "video",
                "profile",
                "programScene",
                "scenes",
                "inputs",
                "stream",
                "record",
                "recordDirectory",
                "stats",
                "streamService",
                "streamArm",
                "unread",
            ],
            Names(CliJson.Serialize(inspection))
        );
    }

    [Fact]
    public void ObsValidate_KeepsItsFields()
    {
        ObsValidation validation = ObsValidator.Unavailable(
            null,
            ObsUnavailableException.Unreachable,
            "OBS is not running."
        );

        Assert.IsAssignableFrom<ICliResult>(validation);
        Assert.Equal(
            [
                "schemaVersion",
                "ok",
                "code",
                "error",
                "endpoint",
                "packagedCollection",
                "assetRoot",
                "dataDirectory",
                "errors",
                "warnings",
                "findings",
            ],
            Names(CliJson.Serialize(validation))
        );
    }

    [Fact]
    public void ObsBundle_KeepsItsFields()
    {
        var report = new ObsBundleReport(1, true, null, "manifest", "obs", 3, [], "ok");

        Assert.IsAssignableFrom<ICliResult>(report);
        Assert.Equal(
            ["schemaVersion", "ok", "code", "format", "manifest", "files", "problems", "message"],
            Names(CliJson.Serialize(report))
        );
    }

    [Fact]
    public void Serialize_WritesTheRuntimeType_EnumsAsCamelCaseStrings()
    {
        ICliResult result = new CliResult<ServiceRoleState>
        {
            Ok = true,
            Details = ServiceRoleState.Ready,
        };

        using JsonDocument json = JsonDocument.Parse(CliJson.Serialize(result));

        Assert.Equal("ready", json.RootElement.GetProperty("details").GetString());
    }

    [Fact]
    public void Environment_IsHeroesReplayEnv()
    {
        Assert.Equal(
            System.Environment.GetEnvironmentVariable("HEROES_REPLAY_ENV"),
            CliJson.CurrentEnvironment()
        );
    }

    private static string[] Names(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    }
}
