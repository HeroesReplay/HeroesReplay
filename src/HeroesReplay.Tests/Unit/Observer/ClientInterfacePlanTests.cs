using System;
using System.IO;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientInterfacePlanTests
{
    [Theory]
    [InlineData(true, false, true, ClientPresetAction.Keep)]
    [InlineData(true, false, false, ClientPresetAction.Write)]
    [InlineData(false, false, true, ClientPresetAction.Write)]
    [InlineData(false, false, false, ClientPresetAction.Write)]
    [InlineData(true, true, true, ClientPresetAction.Keep)]
    [InlineData(false, true, true, ClientPresetAction.LeaveRunning)]
    [InlineData(true, true, false, ClientPresetAction.LeaveRunning)]
    [InlineData(false, true, false, ClientPresetAction.LeaveRunning)]
    public void Preset_WritesANewBuildOnlyWhileHeroesIsStopped(
        bool presetMatches,
        bool heroesRunning,
        bool buildSealed,
        ClientPresetAction expected
    )
    {
        Assert.Equal(
            expected,
            ClientInterfacePlan.Preset(presetMatches, heroesRunning, buildSealed)
        );
    }

    [Theory]
    [InlineData(true, false, true, false, 0, true)]
    [InlineData(true, false, false, true, 0, true)]
    [InlineData(true, true, true, true, 0, false)]
    [InlineData(true, false, false, false, 0, false)]
    [InlineData(false, false, true, true, 0, false)]
    [InlineData(true, false, true, true, 1, false)]
    [InlineData(true, false, true, false, 2, false)]
    public void RestartAfterGameData_RestartsOnceAfterTheDownloadScreenClears(
        bool sawDownload,
        bool downloadVisible,
        bool gameDataStartup,
        bool replayVisible,
        int restarts,
        bool expected
    )
    {
        Assert.Equal(
            expected,
            ClientInterfacePlan.RestartAfterGameData(
                sawDownload,
                downloadVisible,
                gameDataStartup,
                replayVisible,
                restarts
            )
        );
    }

    [Fact]
    public void ExtendForGameDataDownload_DoesNotPassTheCap()
    {
        DateTimeOffset started = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset deadline = started.AddMinutes(4);
        DateTimeOffset nearCap = started
            .Add(ClientInterfacePlan.GameDataDownloadCap)
            .AddMinutes(-1);

        DateTimeOffset extended = ClientInterfacePlan.ExtendForGameDataDownload(
            started,
            deadline,
            nearCap
        );

        Assert.Equal(started.Add(ClientInterfacePlan.GameDataDownloadCap), extended);
    }

    [Fact]
    public void ExtendForGameDataDownload_KeepsALaterDeadline()
    {
        DateTimeOffset started = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset deadline = started.AddMinutes(10);

        DateTimeOffset extended = ClientInterfacePlan.ExtendForGameDataDownload(
            started,
            deadline,
            started
        );

        Assert.Equal(deadline, extended);
    }

    [Fact]
    public void ExtendForGameDataDownload_MovesTheDeadlineForward()
    {
        DateTimeOffset started = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = started.AddMinutes(20);

        DateTimeOffset extended = ClientInterfacePlan.ExtendForGameDataDownload(
            started,
            started.AddMinutes(4),
            now
        );

        Assert.Equal(now.Add(ClientInterfacePlan.GameDataDownloadExtension), extended);
    }

    [Fact]
    public void Newest_SelectsTheHighestInstalledBuild()
    {
        string newest = ClientInterfacePlan.Newest(
            new[] { "2.55.17.98025", "2.57.0.98285", "2.57.0.98304", "2.55.17.97650" }
        );

        Assert.Equal("2.57.0.98304", newest);
    }

    [Fact]
    public void Newest_SkipsBlankEntries()
    {
        Assert.Null(ClientInterfacePlan.Newest(null));
        Assert.Null(ClientInterfacePlan.Newest(new[] { " ", null }));
        Assert.Equal("2.55.17.98025", ClientInterfacePlan.Newest(new[] { "", "2.55.17.98025" }));
    }

    [Theory]
    [InlineData("2.57.0.98304", "2.57.0.98304", true)]
    [InlineData("2.57.0.98304 ", "2.57.0.98304", true)]
    [InlineData("2.57.0.98285", "2.57.0.98304", false)]
    [InlineData(null, "2.57.0.98304", false)]
    [InlineData("2.57.0.98304", null, true)]
    [InlineData("2.57.0.98304", " ", true)]
    public void IsSealed_RequiresTheNewestInstalledBuild(
        string recorded,
        string newest,
        bool expected
    )
    {
        Assert.Equal(expected, ClientInterfacePlan.IsSealed(recorded, newest));
    }

    [Fact]
    public void Seal_RoundTripsTheBuildThatWasWritten()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-interface-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            string path = ClientInterfaceSeal.FilePath(directory);
            Assert.Null(ClientInterfaceSeal.Read(path));

            ClientInterfaceSeal.Write(path, " 2.57.0.98304 ");

            Assert.Equal("2.57.0.98304", ClientInterfaceSeal.Read(path));
            Assert.True(
                ClientInterfacePlan.IsSealed(ClientInterfaceSeal.Read(path), "2.57.0.98304")
            );
            Assert.False(
                ClientInterfacePlan.IsSealed(ClientInterfaceSeal.Read(path), "2.57.0.98305")
            );
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
