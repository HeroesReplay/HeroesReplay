using System;
using System.IO;
using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientInterfacePlanTests
{
    [Theory]
    [InlineData(
        ReplayClientPatch.Previous,
        "AhliObs 0.75.StormInterface",
        "AhliObs 0.75",
        "AhliObs 0.75.StormInterface"
    )]
    [InlineData(
        ReplayClientPatch.Current,
        "AhliObs 0.75.StormInterface",
        "AhliObs 0.75",
        "AhliObs 0.75"
    )]
    [InlineData(ReplayClientPatch.Current, "AhliObs 0.75", null, "AhliObs 0.75")]
    public void ReplayInterfaceForLaunch_PreviousPatchReadsTheRootFile(
        ReplayClientPatch patch,
        string root,
        string account,
        string expected
    )
    {
        Assert.Equal(expected, ClientInterfacePlan.ReplayInterfaceForLaunch(patch, root, account));
    }

    [Theory]
    [InlineData("AhliObs 0.75.StormInterface", "AhliObs 0.75", true)]
    [InlineData("AhliObs 0.75.StormInterface", "", true)]
    [InlineData("AhliObs 0.75.StormInterface", null, true)]
    [InlineData("AhliObs 0.75.StormInterface", "AhliObs 0.75.StormInterface", false)]
    public void LoadsDefaultHud_BareDropdownNameIsTheDefaultHud(
        string expected,
        string actual,
        bool loadsDefault
    )
    {
        Assert.Equal(loadsDefault, ClientInterfacePlan.LoadsDefaultHud(expected, actual));
    }

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

    /// <summary>
    /// #206: the stream PC's drifted root file (blank interfaces, no background audio) is a
    /// write while Heroes is closed, which is when the next replay's client is prepared.
    /// </summary>
    [Fact]
    public void Preset_RepairsADriftedRootFileOnlyWhileHeroesIsClosed()
    {
        Assert.Equal(
            ClientPresetAction.Write,
            ClientInterfacePlan.Preset(presetMatches: false, heroesRunning: false, true)
        );
        Assert.Equal(
            ClientPresetAction.LeaveRunning,
            ClientInterfacePlan.Preset(presetMatches: false, heroesRunning: true, true)
        );
    }

    [Theory]
    [InlineData(65675662, 65675662, true)]
    [InlineData(65675662, 65675674, false)]
    [InlineData(65675662, null, false)]
    [InlineData(null, null, false)]
    public void CheckedBeforeLaunch_OnlyForTheReplayPreparedDuringTheReport(
        int? replayId,
        int? checkedFor,
        bool expected
    )
    {
        Assert.Equal(expected, ClientInterfacePlan.CheckedBeforeLaunch(replayId, checkedFor));
    }

    [Theory]
    [InlineData(true, false, true, false, 0)]
    [InlineData(true, false, false, true, 0)]
    [InlineData(true, true, true, true, 0)]
    [InlineData(true, false, false, false, 0)]
    [InlineData(false, false, true, true, 0)]
    [InlineData(true, false, true, true, 1)]
    [InlineData(true, false, true, false, 2)]
    public void RestartAfterGameData_LeavesTheSwitcherClientRunning(
        bool sawDownload,
        bool downloadVisible,
        bool gameDataStartup,
        bool replayVisible,
        int restarts
    )
    {
        Assert.False(
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
    public void RestartAfterGameData_LeavesTheNewestExeAloneWhileItHandsOff()
    {
        Assert.False(
            ClientInterfacePlan.RestartAfterGameData(
                sawDownload: true,
                downloadVisible: false,
                gameDataStartup: true,
                replayVisible: false,
                restarts: 0,
                clientBuildMatches: false
            )
        );
        Assert.False(
            ClientInterfacePlan.RestartAfterGameData(
                sawDownload: true,
                downloadVisible: false,
                gameDataStartup: true,
                replayVisible: false,
                restarts: 0,
                clientBuildMatches: true
            )
        );
        Assert.False(ClientInterfacePlan.MayAcceptReplayScreen(false, true));
        Assert.True(ClientInterfacePlan.MayAcceptReplayScreen(true, true));
        Assert.False(ClientInterfacePlan.MayAcceptReplayScreen(true, false));
        Assert.False(ClientInterfacePlan.DownloadBelongsToReplayClient(true, false));
        Assert.True(ClientInterfacePlan.DownloadBelongsToReplayClient(true, true));
        Assert.False(ClientInterfacePlan.DownloadBelongsToReplayClient(false, true));
    }

    [Fact]
    public void MatchingExe_StaysUpAfterPreparingGameData()
    {
        bool latched = ClientInterfacePlan.LatchGameDataStartup(
            alreadyLatched: false,
            gameDataStartup: true,
            clientBuildMatches: false,
            differentBuild: true
        );
        Assert.False(latched);

        latched = ClientInterfacePlan.LatchGameDataStartup(
            latched,
            gameDataStartup: true,
            clientBuildMatches: true,
            differentBuild: false
        );
        Assert.True(latched);
        Assert.False(
            ClientInterfacePlan.RestartAfterGameData(
                sawDownload: false,
                downloadVisible: false,
                gameDataStartup: true,
                replayVisible: false,
                restarts: 0,
                clientBuildMatches: true,
                sawGameDataStartup: latched
            )
        );
        Assert.False(
            ClientInterfacePlan.OwesObserverRestart(true, latched, gameDataStartup: true, 0)
        );

        latched = ClientInterfacePlan.LatchGameDataStartup(
            latched,
            gameDataStartup: false,
            clientBuildMatches: false,
            differentBuild: false
        );
        Assert.True(latched);

        Assert.False(
            ClientInterfacePlan.RestartAfterGameData(
                sawDownload: false,
                downloadVisible: false,
                gameDataStartup: true,
                replayVisible: true,
                restarts: 0,
                clientBuildMatches: true,
                sawGameDataStartup: latched
            )
        );
        Assert.False(
            ClientInterfacePlan.OwesObserverRestart(true, latched, gameDataStartup: true, 0)
        );

        latched = ClientInterfacePlan.LatchGameDataStartup(
            latched,
            gameDataStartup: false,
            clientBuildMatches: true,
            differentBuild: false
        );
        Assert.False(
            ClientInterfacePlan.RestartAfterGameData(
                sawDownload: false,
                downloadVisible: false,
                gameDataStartup: false,
                replayVisible: true,
                restarts: 0,
                clientBuildMatches: true,
                sawGameDataStartup: latched
            )
        );
        Assert.False(
            ClientInterfacePlan.RestartAfterGameData(
                sawDownload: false,
                downloadVisible: false,
                gameDataStartup: false,
                replayVisible: true,
                restarts: 0,
                clientBuildMatches: false,
                sawGameDataStartup: true
            )
        );
        Assert.False(
            ClientInterfacePlan.RestartAfterGameData(
                sawDownload: false,
                downloadVisible: true,
                gameDataStartup: false,
                replayVisible: true,
                restarts: 0,
                clientBuildMatches: true,
                sawGameDataStartup: true
            )
        );
        Assert.False(
            ClientInterfacePlan.RestartAfterGameData(
                sawDownload: false,
                downloadVisible: false,
                gameDataStartup: false,
                replayVisible: true,
                restarts: 1,
                clientBuildMatches: true,
                sawGameDataStartup: true
            )
        );
        Assert.False(
            ClientInterfacePlan.OwesObserverRestart(true, true, gameDataStartup: false, 1)
        );
        Assert.False(
            ClientInterfacePlan.RestartAfterGameData(
                sawDownload: false,
                downloadVisible: false,
                gameDataStartup: false,
                replayVisible: true,
                restarts: 0,
                clientBuildMatches: true,
                sawGameDataStartup: false
            )
        );
        Assert.False(
            ClientInterfacePlan.OwesObserverRestart(true, false, gameDataStartup: false, 0)
        );
        Assert.False(
            ClientInterfacePlan.LatchGameDataStartup(
                alreadyLatched: true,
                gameDataStartup: false,
                clientBuildMatches: false,
                differentBuild: true
            )
        );
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
