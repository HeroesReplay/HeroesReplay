using System;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Retention;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Replays;

/// <summary>
/// #493: the replay, client and heroes-data caches. The media pruner only ever removed a played
/// replay, nothing removed a retained client build, and heroes-data was never refreshed.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ReplayCacheCleanupTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Old = Now.AddDays(-20);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-caches-" + Guid.NewGuid().ToString("N")
    );

    public ReplayCacheCleanupTests() => Directory.CreateDirectory(root);

    public void Dispose() => TestTemp.Delete(root);

    private string Standard => Path.Combine(root, "Standard");
    private string Requests => Path.Combine(root, "Requests");

    [Fact]
    public void Plan_RemovesReplaysThatWillNeverPlay_AndKeepsWaitingRequests()
    {
        Write(Standard, "100_SL_Gold_Dragon Shire_.StormReplay", Old);
        string unplayed = Write(Standard, "101_SL_Gold_Dragon Shire_.StormReplay", Old);
        Write(Standard, "102_SL_Gold_Dragon Shire_.StormReplay", Now.AddDays(-2));
        Write(Standard, "103_SL_Gold_Dragon Shire_.StormReplay", Old);
        string quarantined = Write(Standard, "104_SL_Gold_Dragon Shire_.StormReplay", Old);
        string waitingRequest = Write(Requests, "200_QM_Gold_Dragon Shire_.StormReplay", Old);
        string brokenRequest = Write(Requests, "201_QM_Gold_Dragon Shire_.StormReplay", Old);
        string playedSidecar = Write(Requests, "202_QM_Gold_Dragon Shire_.request.json", Old);
        string waitingSidecar = Write(Requests, "200_QM_Gold_Dragon Shire_.request.json", Old);
        string orphanSidecar = Write(Requests, "203_QM_Gold_Dragon Shire_.request.json", Old);
        string youngSidecar = Write(
            Requests,
            "204_QM_Gold_Dragon Shire_.request.json",
            Now.AddDays(-1)
        );
        File.WriteAllText(Path.Combine(root, "spectated-ids.txt"), "100\n202\n");
        File.WriteAllText(Path.Combine(root, "quarantine-ids.txt"), "104\n201\n");
        Write(Path.Combine(root, "leases"), "103.txt", Now.AddHours(-1));

        PrunePlan plan = ReplayCacheCleanup.Plan(Settings(), Now);

        Assert.Equal(
            new[] { unplayed, quarantined, brokenRequest, playedSidecar, orphanSidecar }.Order(),
            plan.Items.Select(item => item.Path).Order()
        );
        Assert.Contains("never played", plan.Items.Single(item => item.Path == unplayed).Reason);
        Assert.Contains(
            "quarantined",
            plan.Items.Single(item => item.Path == brokenRequest).Reason
        );
        Assert.DoesNotContain(
            plan.Items,
            item =>
                item.Path == waitingRequest
                || item.Path == waitingSidecar
                || item.Path == youngSidecar
        );
    }

    [Fact]
    public void ClientBuilds_OnlyCopiesBelowThePatchLineGo_NeverAnInstalledOne()
    {
        string archive = Path.Combine(root, "Clients");
        foreach (
            string build in new[]
            {
                "Base97605",
                "Base98285",
                "Base98304",
                "Base98348",
                "Base96000",
            }
        )
        {
            Write(Path.Combine(archive, build), InstalledClientCatalog.ExeFileName, Old);
            Directory.SetLastWriteTimeUtc(Path.Combine(archive, build), Old.UtcDateTime);
        }

        string versions(string exe) =>
            Path.GetFileName(Path.GetDirectoryName(exe)) switch
            {
                "Base97605" => "2.55.17.97605",
                "Base96000" => "2.55.16.96000",
                string folder => "2.57.0." + folder.Substring(4),
            };

        PrunePlan plan = ClientBuildArchive.Plan(
            archive,
            "2.57.0.98285",
            installed: ["2.55.16.96000", "2.57.0.98348"],
            keepDays: 7,
            Now,
            versions
        );

        PruneItem item = Assert.Single(plan.Items);
        Assert.Equal(Path.Combine(archive, "Base97605"), item.Path);
        Assert.Contains("below the Spectate:MinimumGameVersion patch line", item.Reason);
    }

    [Fact]
    public void HeroesData_KeepsTheNewestBuilds_AndDropsOlderOnesAndZips()
    {
        string data = Path.Combine(root, "HeroesData");
        foreach (int build in new[] { 98285, 98304, 98348 })
        {
            Write(
                Path.Combine(data, "data"),
                $"herodata_{build}.json",
                Old,
                "{\"meta\":{},\"itemsType\":\"hero\"}"
            );
            Write(Path.Combine(data, "gamestrings"), $"gamestrings_{build}_enus.json", Old, "{}");
        }

        string zip = Write(data, "heroes-data-no-maps-2.57.0.98304.zip", Old);
        string downloading = Write(
            data,
            "heroes-data-no-maps-2.57.0.98348.zip",
            Now.AddMinutes(-2)
        );

        PrunePlan plan = HeroesDataCleanup.Plan(data, retainedVersions: 2, Now);

        Assert.Equal(
            new[]
            {
                zip,
                Path.Combine(data, "data", "herodata_98285.json"),
                Path.Combine(data, "gamestrings", "gamestrings_98285_enus.json"),
            }.Order(),
            plan.Items.Select(item => item.Path).Order()
        );
        Assert.DoesNotContain(plan.Items, item => item.Path == downloading);
    }

    /// <summary>
    /// The layout after #493: the first download flat, each refresh published whole under
    /// builds\, and a crashed download's staging folder. The newest two builds stay, an old
    /// published build goes as one folder, and an old staging folder goes.
    /// </summary>
    [Fact]
    public void HeroesData_RemovesOldPublishedBuildsWhole_AndStaleStaging()
    {
        string data = Path.Combine(root, "HeroesData");
        Write(Path.Combine(data, "data"), "herodata_98285.json", Old, HeroDocument);
        string builds = Path.Combine(data, GameData.BuildsFolderName);
        Write(Path.Combine(builds, "98304", "data"), "herodata_98304.json", Old, HeroDocument);
        Write(
            Path.Combine(builds, "98304", "gamestrings"),
            "gamestrings_98304_enus.json",
            Old,
            "{}"
        );
        Write(Path.Combine(builds, "98348", "data"), "herodata_98348.json", Old, HeroDocument);
        string stale = Path.Combine(data, GameData.StagingPrefix + "old");
        Write(Path.Combine(stale, "build", "data"), "herodata_99999.json", Old, HeroDocument);
        Directory.SetLastWriteTimeUtc(stale, Old.UtcDateTime);
        string fresh = Path.Combine(data, GameData.StagingPrefix + "now");
        Write(Path.Combine(fresh, "build", "data"), "herodata_99999.json", Now, HeroDocument);
        Directory.SetLastWriteTimeUtc(fresh, Now.UtcDateTime);

        PrunePlan plan = HeroesDataCleanup.Plan(data, retainedVersions: 2, Now);

        // A staging folder is never a build: 99999 does not count against the newest two.
        Assert.Equal(
            new[] { stale, Path.Combine(data, "data", "herodata_98285.json") }.Order(),
            plan.Items.Select(item => item.Path).Order()
        );

        PrunePlan one = HeroesDataCleanup.Plan(data, retainedVersions: 1, Now);

        Assert.Contains(
            one.Items,
            item => item.Path == Path.Combine(builds, "98304") && item.IsDirectory
        );
        Assert.DoesNotContain(
            one.Items,
            item => item.Path.StartsWith(Path.Combine(builds, "98348"))
        );
    }

    /// <summary>
    /// The new-patch race: while any process refreshes or loads heroes-data2 (it holds the lock),
    /// the pruner removes nothing, so it never takes a build out from under a role.
    /// </summary>
    [Fact]
    public void HeroesData_NothingIsRemovedWhileARoleHoldsTheLock()
    {
        string data = Path.Combine(root, "HeroesData");
        foreach (int build in new[] { 98285, 98304, 98348 })
        {
            Write(Path.Combine(data, "data"), $"herodata_{build}.json", Old, HeroDocument);
        }

        using (
            FileStream held = Core.Shared.DurableFile.TryLock(
                Path.Combine(data, GameData.LockFileName)
            )
        )
        {
            Assert.NotNull(held);
            Assert.True(GameData.IsBusy(data));
            Assert.Empty(HeroesDataCleanup.Plan(data, retainedVersions: 1, Now).Items);
        }

        Assert.False(GameData.IsBusy(data));
        Assert.Equal(2, HeroesDataCleanup.Plan(data, retainedVersions: 1, Now).Items.Count);
    }

    [Fact]
    public void HeroesData_PublishMovesAWholeBuildInByOneRename_AndKeepsOneAlreadyThere()
    {
        string data = Path.Combine(root, "HeroesData");
        string extracted = Path.Combine(data, GameData.StagingPrefix + "a", "build");
        Write(Path.Combine(extracted, "data"), "herodata_98348.json", Now, HeroDocument);
        Write(Path.Combine(extracted, "gamestrings"), "gamestrings_98348_enus.json", Now, "{}");

        string published = GameData.Publish(data, extracted, 98348);

        Assert.Equal(Path.Combine(data, GameData.BuildsFolderName, "98348"), published);
        Assert.True(File.Exists(Path.Combine(published, "data", "herodata_98348.json")));
        Assert.True(
            File.Exists(Path.Combine(published, "gamestrings", "gamestrings_98348_enus.json"))
        );
        Assert.False(Directory.Exists(extracted));

        string again = Path.Combine(data, GameData.StagingPrefix + "b", "build");
        Write(Path.Combine(again, "data"), "herodata_98348.json", Now, "{}");

        Assert.Equal(published, GameData.Publish(data, again, 98348));
        Assert.Equal(
            HeroDocument,
            File.ReadAllText(Path.Combine(published, "data", "herodata_98348.json"))
        );
    }

    private const string HeroDocument = "{\"meta\":{},\"itemsType\":\"hero\"}";

    [Theory]
    [InlineData("v2.57.0.98348", 98348)]
    [InlineData("v2.57.0.98217_ptr", -1)]
    [InlineData("", -1)]
    [InlineData("latest", -1)]
    public void HeroesData_OnlyALiveReleaseBuildReplacesTheLocalData(string tag, int build)
    {
        Assert.Equal(build, GameData.ReleaseBuild(tag));
    }

    [Fact]
    public void HeroesData_RefreshIsDueOncePerInterval_AndNeverWhenOff()
    {
        string data = Path.Combine(root, "HeroesData");
        Directory.CreateDirectory(data);

        Assert.False(GameData.RefreshDue(data, TimeSpan.Zero, Now));
        Assert.True(GameData.RefreshDue(data, TimeSpan.FromDays(1), Now));

        Write(data, GameData.RefreshStampFileName, Now.AddHours(-2));

        Assert.False(GameData.RefreshDue(data, TimeSpan.FromDays(1), Now));
        Assert.True(GameData.RefreshDue(data, TimeSpan.FromHours(1), Now));
    }

    private AppSettings Settings() =>
        new()
        {
            Location = new LocationSettings { DataDirectory = root },
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                StandardCacheDirectoryName = "Standard",
                RequestsCacheDirectoryName = "Requests",
            },
            Retention = new RetentionSettings { UnplayedReplayKeepDays = 14, ReplayKeepDays = 14 },
        };

    private static string Write(
        string folder,
        string name,
        DateTimeOffset written,
        string text = "x"
    )
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
        return path;
    }
}
