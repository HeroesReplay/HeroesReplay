using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientDownloadHoldTests : IDisposable
{
    private const string Floor = "2.57.0.98285";
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 14, 42, 35, TimeSpan.Zero);
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(10);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-download-hold-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Check_WaitsForTheExeThenHandsOverToThePreviousPatchRules()
    {
        // Live: the switcher open at 14:42:35, Base98285's exe at 14:43:16.
        Assert.Equal(
            BuildDownloadState.Waiting,
            ClientDownloadHold.Check(
                ReplayClientPatch.Download,
                handedToSwitcher: true,
                exeExists: false,
                TimeSpan.FromSeconds(40),
                Limit
            )
        );
        Assert.Equal(
            BuildDownloadState.Arrived,
            ClientDownloadHold.Check(
                ReplayClientPatch.Download,
                handedToSwitcher: true,
                exeExists: true,
                TimeSpan.FromSeconds(41),
                Limit
            )
        );
    }

    [Fact]
    public void Check_FailsWhenTheExeNeverAppears()
    {
        Assert.Equal(
            BuildDownloadState.Waiting,
            ClientDownloadHold.Check(
                ReplayClientPatch.Download,
                true,
                false,
                TimeSpan.FromMinutes(9.9),
                Limit
            )
        );
        Assert.Equal(
            BuildDownloadState.Failed,
            ClientDownloadHold.Check(ReplayClientPatch.Download, true, false, Limit, Limit)
        );
        // Zero or less is the default limit.
        Assert.Equal(TimeSpan.FromMinutes(10), ClientDownloadHold.DownloadLimit(TimeSpan.Zero));
        Assert.Equal(
            BuildDownloadState.Waiting,
            ClientDownloadHold.Check(
                ReplayClientPatch.Download,
                true,
                false,
                TimeSpan.FromMinutes(5),
                TimeSpan.Zero
            )
        );
    }

    [Theory]
    [InlineData(ReplayClientPatch.Current)]
    [InlineData(ReplayClientPatch.Previous)]
    [InlineData(ReplayClientPatch.NotInstalled)]
    public void Check_OnlyAMissingOlderBuildDownloads(ReplayClientPatch patch)
    {
        Assert.Equal(
            BuildDownloadState.NotDownloading,
            ClientDownloadHold.Check(patch, true, false, TimeSpan.FromHours(1), Limit)
        );
        Assert.Equal(
            BuildDownloadState.NotDownloading,
            ClientDownloadHold.Check(
                ReplayClientPatch.Download,
                handedToSwitcher: false,
                exeExists: false,
                TimeSpan.FromHours(1),
                Limit
            )
        );
    }

    [Fact]
    public void DialogFailsDownload_OnlyAMismatchBeforeTheExeArrives()
    {
        Assert.True(
            ClientDownloadHold.DialogFailsDownload(
                BuildDownloadState.Waiting,
                ClientHoldReason.VersionMismatch
            )
        );
        Assert.False(
            ClientDownloadHold.DialogFailsDownload(
                BuildDownloadState.Arrived,
                ClientHoldReason.VersionMismatch
            )
        );
        Assert.False(
            ClientDownloadHold.DialogFailsDownload(
                BuildDownloadState.NotDownloading,
                ClientHoldReason.VersionMismatch
            )
        );
        Assert.False(
            ClientDownloadHold.DialogFailsDownload(
                BuildDownloadState.Waiting,
                ClientHoldReason.RegionUnavailable
            )
        );
    }

    [Fact]
    public void Active_HoldsAFailedBuildForTheHoldWindow()
    {
        var failures = new Dictionary<string, DateTimeOffset>
        {
            ["2.57.0.98285"] = Now,
            ["2.57.0.98290"] = Now - TimeSpan.FromHours(4),
            ["2.57.0.98295"] = Now - TimeSpan.FromHours(3.9),
        };

        IReadOnlyList<string> held = ClientDownloadHold.Active(
            failures,
            Now,
            TimeSpan.FromHours(4)
        );

        Assert.Equal(new[] { "2.57.0.98285", "2.57.0.98295" }, held);
        Assert.Equal(TimeSpan.FromHours(4), ClientDownloadHold.Hold(TimeSpan.Zero));
        Assert.Empty(ClientDownloadHold.Active(null, Now, TimeSpan.FromHours(4)));
    }

    [Fact]
    public void Record_PersistsTheBuildForTheOtherProcessesAndDropsEndedHolds()
    {
        string path = ClientDownloadHold.FilePath(root);
        TimeSpan hold = TimeSpan.FromHours(4);
        ClientDownloadHold.Record(path, "2.57.0.98290", Now - TimeSpan.FromHours(5), hold);

        ClientDownloadHold.Record(path, "2, 57, 0, 98285", Now, hold);

        IReadOnlyDictionary<string, DateTimeOffset> read = ClientDownloadHold.Read(path);
        Assert.Equal(Now, Assert.Single(read).Value);
        Assert.Equal("2.57.0.98285", Assert.Single(read).Key);
        Assert.Equal(new[] { "2.57.0.98285" }, ClientDownloadHold.ActiveIn(root, Now, hold));
        Assert.Empty(ClientDownloadHold.ActiveIn(root, Now + hold, hold));
        Assert.Equal(
            ReplayClientPatch.NotInstalled,
            ReplayClientRoute.Classify(
                "2.57.0.98285",
                new[] { "2.57.0.98348" },
                ClientDownloadHold.ActiveIn(root, Now.AddMinutes(30), hold)
            )
        );
    }

    [Fact]
    public void Read_AnUnreadableFileHoldsNothingAndIsMovedAside()
    {
        Directory.CreateDirectory(root);
        string path = ClientDownloadHold.FilePath(root);
        File.WriteAllText(path, "{ not json");

        Assert.Empty(ClientDownloadHold.Read(path));
        Assert.False(File.Exists(path));
        Assert.Empty(ClientDownloadHold.ActiveIn(null, Now, TimeSpan.FromHours(4)));
        Assert.Empty(ClientDownloadHold.ActiveIn(root, Now, TimeSpan.FromHours(4)));
    }

    [Fact]
    public void MissingBuildWithARetainedCopy_IsReclaimedBeforeTheSwitcherOpens()
    {
        string archive = Path.Combine(root, "archive");
        string game = Path.Combine(root, "game");
        string kept = Path.Combine(archive, "Base98285", InstalledClientCatalog.ExeFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(kept));
        File.WriteAllText(kept, "kept-98285");
        Directory.CreateDirectory(Path.Combine(game, "Versions", "Base98285"));
        string live = InstalledClientCatalog.ExePathFor(game, "2.57.0.98285");

        // The launch restores before it classifies, so the copy wins over a download.
        Assert.True(ClientBuildArchive.Restore(game, archive, "2.57.0.98285"));
        Assert.Equal("kept-98285", File.ReadAllText(live));
        Assert.Equal(
            ReplayClientPatch.Previous,
            ReplayClientRoute.Classify("2.57.0.98285", new[] { "2.57.0.98348", "2.57.0.98285" })
        );
    }

    [Fact]
    public void ABuildBlizzardDownloaded_IsKeptAtTheNextLaunch()
    {
        string archive = Path.Combine(root, "archive");
        string game = Path.Combine(root, "game");
        string downloaded = InstalledClientCatalog.ExePathFor(game, "2.57.0.98285");
        Directory.CreateDirectory(Path.GetDirectoryName(downloaded));
        File.WriteAllText(downloaded, "downloaded-98285");

        IReadOnlyList<ClientBuildKeepItem> kept = ClientBuildArchive.Preserve(
            new[] { new InstalledClient("2.57.0.98285", downloaded) },
            archive,
            Floor
        );

        Assert.Equal(ClientBuildKeep.Copied, Assert.Single(kept).Result);
        Assert.Equal(
            "downloaded-98285",
            File.ReadAllText(Path.Combine(archive, "Base98285", InstalledClientCatalog.ExeFileName))
        );
    }

    [Fact]
    public void ExePathFor_IsTheBuildsBaseFolder()
    {
        Assert.Equal(
            Path.Combine(@"C:\Games\Heroes", "Versions", "Base98285", "HeroesOfTheStorm_x64.exe"),
            InstalledClientCatalog.ExePathFor(@"C:\Games\Heroes", "2.57.0.98285")
        );
        Assert.Null(InstalledClientCatalog.ExePathFor(@"C:\Games\Heroes", "beta"));
        Assert.Null(InstalledClientCatalog.ExePathFor(null, "2.57.0.98285"));
    }
}
