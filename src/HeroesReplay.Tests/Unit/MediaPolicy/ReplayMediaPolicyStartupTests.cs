using System;
using System.IO;
using HeroesReplay.CLI;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.MediaPolicy;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HeroesReplay.Tests.Unit.MediaPolicy;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayMediaPolicyStartupTests
{
    private static readonly DateTime Now = new DateTime(2026, 9, 29, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void MissingSection_DefaultsToDisabledAndDoesNotThrow()
    {
        ReplayMediaPolicySettings settings = ReplayMediaPolicyStartup.Require(Config("{}"));

        Assert.Equal(ReplayRecordingMode.Disabled, settings.RecordingMode);
        Assert.Equal(ReplayPublicationMode.Disabled, settings.PublicationMode);
        Assert.Equal("1", settings.Version);
    }

    [Fact]
    public void IntegerRecordingMode_FailsClosed()
    {
        IConfiguration configuration = Config(
            """
            {
              "ReplayMedia": {
                "RecordingMode": "3",
                "PublicationMode": "Disabled"
              }
            }
            """
        );

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            ReplayMediaPolicyStartup.Require(configuration)
        );
        ReplayMediaPolicySettings settings = ReplayMediaPolicyStartup.Bind(
            configuration,
            out var errors
        );

        Assert.Contains(ReplayMediaConfigurationError.RecordingModeInvalid, error.Message);
        Assert.Contains(ReplayMediaConfigurationError.RecordingModeInvalid, errors);
        Assert.DoesNotContain(ReplayMediaConfigurationError.PublicationModeInvalid, errors);
        AssertNotEligible(settings);
    }

    [Fact]
    public void BindSettings_RejectsIntegerModeBeforeTheGenericBinder()
    {
        IConfiguration configuration = Config(
            """
            {
              "ReplayMedia": {
                "RecordingMode": "3",
                "PublicationMode": "AllEligible"
              }
            }
            """
        );

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            ServiceCollectionExtensions.BindSettings(configuration)
        );

        Assert.Contains(ReplayMediaConfigurationError.RecordingModeInvalid, error.Message);
    }

    [Fact]
    public void UnreadableAge_IsNotReportedAsNegative()
    {
        ReplayMediaPolicyStartup.Bind(
            Config(
                """
                { "ReplayMedia": { "OrdinaryCandidateMaxAge": "abc" } }
                """
            ),
            out var errors
        );

        Assert.Contains(ReplayMediaPolicyStartup.Unreadable + ":OrdinaryCandidateMaxAge", errors);
        Assert.DoesNotContain(ReplayMediaConfigurationError.OrdinaryMaxAgeNegative, errors);
    }

    [Fact]
    public void NegativeAge_StaysAValidationError()
    {
        ReplayMediaPolicyStartup.Bind(
            Config(
                """
                { "ReplayMedia": { "OrdinaryCandidateMaxAge": "-1.00:00:00" } }
                """
            ),
            out var errors
        );

        Assert.Contains(ReplayMediaConfigurationError.OrdinaryMaxAgeNegative, errors);
        Assert.DoesNotContain(
            ReplayMediaPolicyStartup.Unreadable + ":OrdinaryCandidateMaxAge",
            errors
        );
    }

    [Fact]
    public void UnreadableBool_IsNotLabeledAsARecordingMode()
    {
        ReplayMediaPolicyStartup.Bind(
            Config(
                """
                { "ReplayMedia": { "RequireCurrentPatch": "not-a-bool" } }
                """
            ),
            out var errors
        );

        Assert.Contains(ReplayMediaPolicyStartup.Unreadable + ":RequireCurrentPatch", errors);
        Assert.DoesNotContain(ReplayMediaConfigurationError.RecordingModeInvalid, errors);
    }

    [Fact]
    public void EmptyVersion_FailsClosed()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            ReplayMediaPolicyStartup.Require(Config("""{ "ReplayMedia": { "Version": " " } }"""))
        );

        Assert.Contains(ReplayMediaConfigurationError.ConfigurationVersionMissing, error.Message);
    }

    [Fact]
    public void RecordingDisabledWhilePublishing_FailsClosed()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            ReplayMediaPolicyStartup.Require(
                Config(
                    """
                    {
                      "ReplayMedia": {
                        "RecordingMode": "Disabled",
                        "PublicationMode": "Curated"
                      }
                    }
                    """
                )
            )
        );

        Assert.Contains(
            ReplayMediaConfigurationError.RecordingDisabledWhilePublishing,
            error.Message
        );
    }

    [Fact]
    public void NamedAll_BindsInMemoryOnly()
    {
        ReplayMediaPolicySettings settings = ReplayMediaPolicyStartup.Require(
            Config(
                """
                {
                  "ReplayMedia": {
                    "Version": "1",
                    "RecordingMode": "All",
                    "PublicationMode": "AllEligible"
                  }
                }
                """
            )
        );

        Assert.Equal(ReplayRecordingMode.All, settings.RecordingMode);
        Assert.Equal(ReplayPublicationMode.AllEligible, settings.PublicationMode);
        AssertBaseFileDoesNotSelectAMode();
    }

    [Fact]
    public void DevSettings_RecordAFreshOrdinaryReplay()
    {
        string devPath = Path.Combine(AppContext.BaseDirectory, "appsettings.dev.json");
        string sourcePath = FindRepoFile(
            Path.Combine("src", "HeroesReplay.CLI", "appsettings.dev.json")
        );
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddJsonFile(devPath)
            .Build();

        ReplayMediaPolicySettings settings = ReplayMediaPolicyStartup.Require(configuration);
        ReplayMediaDecision decision = ReplayMediaPolicy.Evaluate(
            new ReplayMediaPolicyInput
            {
                ReplayId = 65550001,
                GameDateUtc = Now,
                GameVersion = "2.57.0.98304",
                Map = "Volskaya Foundry",
                GameMode = "Storm League",
            },
            settings,
            Now
        );

        Assert.True(decision.Record);
        Assert.NotEqual(ReplayMediaReason.RecordingDisabled, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.RecordedAll, decision.RecordingReason);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.AwaitingCompletion, decision.PublicationReason);
        AssertModesSelected(devPath);
        AssertModesSelected(sourcePath);

        AppSettings app = ServiceCollectionExtensions.BindSettings(configuration);
        Assert.Equal(ReplayRecordingMode.All, app.ReplayMedia.RecordingMode);
        Assert.Equal(ReplayPublicationMode.AllEligible, app.ReplayMedia.PublicationMode);
        Assert.True(app.OBS.RecordingEnabled);
        Assert.True(app.YouTube.Enabled);
        Assert.True(app.YouTube.DryRun);
        AssertBaseFileDoesNotSelectAMode();
    }

    [Fact]
    public void BaseSettings_StayRecordingDisabled()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .Build();

        ReplayMediaPolicySettings settings = ReplayMediaPolicyStartup.Require(configuration);
        ReplayMediaDecision decision = ReplayMediaPolicy.Evaluate(
            new ReplayMediaPolicyInput
            {
                ReplayId = 65550001,
                GameDateUtc = Now,
                GameVersion = "2.57.0.98304",
                Map = "Volskaya Foundry",
                GameMode = "Storm League",
            },
            settings,
            Now
        );

        Assert.False(decision.Record);
        Assert.Equal(ReplayMediaReason.RecordingDisabled, decision.RecordingReason);
        Assert.Equal(ReplayRecordingMode.Disabled, settings.RecordingMode);
        Assert.Equal(ReplayPublicationMode.Disabled, settings.PublicationMode);
    }

    private static void AssertNotEligible(ReplayMediaPolicySettings settings)
    {
        ReplayMediaDecision decision = ReplayMediaPolicy.Evaluate(
            new ReplayMediaPolicyInput { ReplayId = 65389750, GameDateUtc = Now },
            settings,
            Now
        );

        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.ConfigurationInvalid, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.ConfigurationInvalid, decision.PublicationReason);
    }

    private static void AssertBaseFileDoesNotSelectAMode()
    {
        AssertModesAbsent(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        string production = FindRepoFile(
            Path.Combine("src", "HeroesReplay.CLI", "appsettings.prod.json")
        );
        string text = File.ReadAllText(production);
        Assert.Contains("\"RecordingMode\": \"All\"", text, StringComparison.Ordinal);
        Assert.Contains("\"PublicationMode\": \"AllEligible\"", text, StringComparison.Ordinal);
        Assert.Contains("\"RecordingEnabled\": true", text, StringComparison.Ordinal);
        Assert.Contains("\"DryRun\": false", text, StringComparison.Ordinal);
    }

    private static void AssertModesSelected(string path)
    {
        string text = File.ReadAllText(path);
        Assert.Contains("\"RecordingMode\": \"All\"", text, StringComparison.Ordinal);
        Assert.Contains("\"PublicationMode\": \"AllEligible\"", text, StringComparison.Ordinal);
    }

    private static string AssertModesAbsent(string path)
    {
        string text = File.ReadAllText(path);
        Assert.DoesNotContain("RecordingMode", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PublicationMode", text, StringComparison.Ordinal);
        return text;
    }

    private static string FindRepoFile(string relative)
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(relative);
    }

    private static IConfiguration Config(string json)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "hr-media-config-" + Guid.NewGuid().ToString("N") + ".json"
        );
        File.WriteAllText(path, json);
        try
        {
            return new ConfigurationBuilder().AddJsonFile(path).Build();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
