using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeroesReplay.CLI.Commands.Obs;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs.Collection;
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
                "obsRestorePending",
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
    public void ServicesEnsure_KeepsItsFields()
    {
        var report = new ServiceEnsureReport { Ok = true, Code = "service.ensure_noop" };

        Assert.IsAssignableFrom<ICliResult>(report);
        Assert.Equal(
            [
                "schemaVersion",
                "ok",
                "code",
                "message",
                "remediation",
                "environment",
                "checkedAt",
                "stopRequested",
                "supervise",
                "supervisorRunning",
                "supervisorAttached",
                "requested",
                "started",
                "roles",
            ],
            Names(report.ToJson())
        );
        Assert.Equal(report.ToJson(), CliJson.Serialize(report));
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
    public void ObsPlan_KeepsItsFields()
    {
        var plan = new ObsCollectionPlanResult
        {
            Ok = true,
            Code = ObsPlanCodes.Changes,
            Message = "1 difference.",
            Collection = @"C:\Users\x\AppData\Roaming\obs-studio\basic\scenes\HeroesReplay.json",
            Template = @"C:\heroesreplay\app\obs\Default.json",
            TemplateSha256 = "AB",
            Base = "install",
            Update = new ObsPlanUpdate { Action = "none", Message = "Nothing to write." },
            Summary = new Dictionary<string, int> { ["operatorOverride"] = 1 },
            Differences =
            [
                new ObsCollectionDifference
                {
                    Kind = ObsDiffKind.OperatorOverride,
                    Source = "hots-logo",
                    Property = "settings.file",
                    Live = "\"C:/heroesreplay/app/obs/hots-logo.png\"",
                    Apply = ObsDiffApply.Keep,
                },
            ],
        };

        Assert.IsAssignableFrom<ICliResult>(plan);
        Assert.Equal(
            [
                "schemaVersion",
                "ok",
                "code",
                "message",
                "collection",
                "template",
                "templateSha256",
                "recordedTemplateSha256",
                "base",
                "obsRunning",
                "update",
                "pendingRollback",
                "summary",
                "differences",
            ],
            Names(plan.ToJson())
        );
        Assert.Equal(plan.ToJson(), CliJson.Serialize(plan));
        // Byte for byte what obs plan printed before it moved onto CliJson.
        Assert.Equal(JsonSerializer.Serialize(plan, BeforeWithEnums), CliJson.Serialize(plan));
        Assert.Contains("\"kind\": \"operatorOverride\"", plan.ToJson(), StringComparison.Ordinal);
        ObsCollectionPlanResult read = ObsCollectionPlanResult.FromJson(plan.ToJson());
        Assert.Equal(ObsPlanCodes.Changes, read.Code);
        Assert.Equal(ObsDiffKind.OperatorOverride, Assert.Single(read.Differences).Kind);
    }

    [Fact]
    public void ObsBackupAndRestore_KeepTheirFields()
    {
        var result = new ObsBackupResult
        {
            Ok = true,
            Code = ObsBackupCodes.BackedUp,
            Message = "Backed up.",
            Collection = @"C:\scenes\HeroesReplay.json",
            Backup = @"C:\backups\scenes-HeroesReplay.json.20261008T140000000Z.bak",
            Backups =
            [
                new ObsBackupInfo
                {
                    Path = @"C:\backups\scenes-HeroesReplay.json.20261008T140000000Z.bak",
                    TakenAtUtc = new DateTime(2026, 10, 8, 14, 0, 0, DateTimeKind.Utc),
                    Bytes = 42,
                    Sha256 = "CD",
                },
            ],
        };

        Assert.IsAssignableFrom<ICliResult>(result);
        Assert.Equal(
            ["schemaVersion", "ok", "code", "message", "collection", "backup", "saved", "backups"],
            Names(result.ToJson())
        );
        Assert.Equal(result.ToJson(), CliJson.Serialize(result));
        Assert.Equal(JsonSerializer.Serialize(result, Before), CliJson.Serialize(result));
    }

    [Fact]
    public void ConfigEffective_KeepsItsFields()
    {
        var effective = new EffectiveConfiguration
        {
            Ok = true,
            Message = "1 keys, 1 redacted.",
            Environment = "dev",
            EnvironmentSource = "HEROES_REPLAY_ENV",
            BasePath = @"C:\heroesreplay\app",
            Layers =
            [
                new ConfigurationLayer(1, ConfigurationLayers.Base, "appsettings.json", true, 1),
            ],
            RedactedCount = 1,
            Settings =
            [
                new EffectiveSetting(
                    "Twitch:AccessToken",
                    ConfigurationRedaction.Set,
                    true,
                    ConfigurationLayers.Base,
                    "appsettings.json",
                    []
                ),
            ],
        };

        Assert.IsAssignableFrom<ICliResult>(effective);
        Assert.Equal(CliJson.SchemaVersion, effective.SchemaVersion);
        Assert.Equal(
            [
                "schemaVersion",
                "ok",
                "code",
                "message",
                "environment",
                "environmentSource",
                "basePath",
                "section",
                "layers",
                "redactedCount",
                "settings",
            ],
            Names(CliJson.Serialize(effective))
        );
        Assert.Equal(JsonSerializer.Serialize(effective, Before), CliJson.Serialize(effective));
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

    /// <summary>The options <c>obs backup</c>, <c>obs restore</c>, and <c>config effective</c> had before #311.</summary>
    private static readonly JsonSerializerOptions Before = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The options <c>obs plan</c> had before #311.</summary>
    private static readonly JsonSerializerOptions BeforeWithEnums = new(Before)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static string[] Names(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    }
}
