using System;
using System.IO;
using HeroesReplay.CLI;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.SelfUpdate;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HeroesReplay.Tests.Unit.SelfUpdate;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class StreamArmMigrationTests
{
    [Theory]
    [InlineData(false, true, false, StreamArmMigrationOutcome.Armed)]
    [InlineData(false, true, true, StreamArmMigrationOutcome.AlreadyArmed)]
    [InlineData(false, false, false, StreamArmMigrationOutcome.StreamingDisabled)]
    [InlineData(false, false, true, StreamArmMigrationOutcome.StreamingDisabled)]
    [InlineData(true, true, false, StreamArmMigrationOutcome.AlreadyMigrated)]
    [InlineData(true, false, false, StreamArmMigrationOutcome.AlreadyMigrated)]
    public void Decide_ArmsOnlyAStreamingInstallOnce(
        bool migrated,
        bool streamingEnabled,
        bool armed,
        StreamArmMigrationOutcome expected
    )
    {
        Assert.Equal(expected, StreamArmMigration.Decide(migrated, streamingEnabled, armed));
    }

    [Fact]
    public void Run_StreamingInstall_ArmsOnceAndADisarmSurvivesTheNextUpdate()
    {
        string root = TempRoot();
        try
        {
            var arm = new ObsStreamArm(Path.Combine(root, ObsStreamArm.FileName));

            StreamArmMigrationResult first = StreamArmMigration.Run(
                arm,
                streamingEnabled: true,
                previousInstall: @"C:\heroesreplay\app"
            );

            Assert.Equal(StreamArmMigrationOutcome.Armed, first.Outcome);
            Assert.True(arm.IsArmed());
            Assert.Contains(@"C:\heroesreplay\app", first.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(StreamArmMigration.MarkerPathFor(arm)));

            arm.Disarm();
            StreamArmMigrationResult second = StreamArmMigration.Run(
                arm,
                streamingEnabled: true,
                previousInstall: @"C:\heroesreplay\app"
            );

            Assert.Equal(StreamArmMigrationOutcome.AlreadyMigrated, second.Outcome);
            Assert.False(arm.IsArmed());
            Assert.Contains("not armed", second.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_NonStreamingInstall_NeverArms()
    {
        string root = TempRoot();
        try
        {
            var arm = new ObsStreamArm(Path.Combine(root, ObsStreamArm.FileName));

            StreamArmMigrationResult result = StreamArmMigration.Run(
                arm,
                streamingEnabled: false,
                previousInstall: @"C:\heroesreplay\app"
            );

            Assert.Equal(StreamArmMigrationOutcome.StreamingDisabled, result.Outcome);
            Assert.False(arm.IsArmed());
            Assert.True(File.Exists(StreamArmMigration.MarkerPathFor(arm)));

            StreamArmMigrationResult later = StreamArmMigration.Run(
                arm,
                streamingEnabled: true,
                previousInstall: @"C:\heroesreplay\app"
            );

            Assert.Equal(StreamArmMigrationOutcome.AlreadyMigrated, later.Outcome);
            Assert.False(arm.IsArmed());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReplacedInstallSettings_UseTheEnvironmentOverlay()
    {
        string root = TempRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "appsettings.json"),
                """{ "OBS": { "StreamingEnabled": false } }"""
            );
            File.WriteAllText(
                Path.Combine(root, "appsettings.prod.json"),
                """{ "OBS": { "StreamingEnabled": true } }"""
            );

            Assert.True(
                ServiceCollectionExtensions
                    .BuildConfiguration(root, "prod")
                    .GetValue<bool>("OBS:StreamingEnabled")
            );
            Assert.False(
                ServiceCollectionExtensions
                    .BuildConfiguration(root, "dev")
                    .GetValue<bool>("OBS:StreamingEnabled")
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MarkerPath_SitsNextToTheArm()
    {
        var arm = new ObsStreamArm(ObsStreamArm.DefaultPath());

        Assert.Equal(
            Path.Combine(
                Path.GetDirectoryName(ObsStreamArm.DefaultPath()),
                StreamArmMigration.MarkerFileName
            ),
            StreamArmMigration.MarkerPathFor(arm)
        );
    }

    private static string TempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-arm-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }
}
