using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.SelfUpdate;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ReleaseCacheTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-update-cache-" + Guid.NewGuid().ToString("N")
    );

    public ReleaseCacheTests() => Directory.CreateDirectory(Updates);

    public void Dispose() => TestTemp.Delete(root);

    private string Updates => Path.Combine(root, "updates");

    /// <summary>
    /// The stream PC on 2026-10-22: 16 releases v1.0.0-614 to v1.0.0-978 and a loose zip, with
    /// v1.0.0-978 installed and app.previous mirrored from v1.0.0-977.
    /// </summary>
    [Fact]
    public void Plan_KeepsTheInstalledAndPreviousRelease_AndRemovesTheOlderOnesAndTheLooseZip()
    {
        foreach (int build in new[] { 614, 672, 801, 950, 977, 978 })
        {
            Release($"v1.0.0-{build}");
        }

        string loose = Write(
            Path.Combine(Updates, "heroesreplay-v1.0.0-672.zip"),
            Now.AddDays(-20)
        );
        File.WriteAllText(Path.Combine(Updates, ReleaseSkipList.FileName), "v1.0.0-951\tx\n");

        PrunePlan plan = ReleaseCache.Plan(Updates, Facts("v1.0.0-978", "v1.0.0-977"), Now);

        Assert.Equal(
            new[] { "v1.0.0-614", "v1.0.0-672", "v1.0.0-801", "v1.0.0-950" },
            RemovedFolders(plan)
        );
        Assert.Contains(plan.Items, item => item.Path == loose);
        Assert.All(
            plan.Items.Where(item => item.IsDirectory && item.Path.EndsWith("v1.0.0-801")),
            item => Assert.Contains("Release:RetainedVersions", item.Reason)
        );
        // The kept releases lose only their zip and extract\: apply-release.ps1 runs from prepared\.
        foreach (string kept in new[] { "v1.0.0-977", "v1.0.0-978" })
        {
            Assert.Contains(
                plan.Items,
                item => item.Path == Path.Combine(Updates, kept, "extract")
            );
            Assert.Contains(
                plan.Items,
                item => item.Path == Path.Combine(Updates, kept, ReleaseSettings.DefaultAssetName)
            );
            Assert.DoesNotContain(
                plan.Items,
                item => item.Path.Contains(Path.Combine(kept, "prepared"))
            );
        }

        // Plan() wrote nothing.
        Assert.True(Directory.Exists(Path.Combine(Updates, "v1.0.0-614")));
        Assert.True(File.Exists(loose));

        RetentionSweep sweep = plan.DeleteAll();

        Assert.Empty(sweep.Warnings);
        Assert.Equal(
            new[] { "v1.0.0-977", "v1.0.0-978" },
            Directory.GetDirectories(Updates).Select(Path.GetFileName).Order()
        );
        Assert.True(
            File.Exists(Path.Combine(Updates, "v1.0.0-978", "prepared", "heroesreplay.exe"))
        );
        Assert.True(File.Exists(Path.Combine(Updates, ReleaseSkipList.FileName)));
    }

    [Fact]
    public void Plan_KeepsAStagedRelease_ButRemovesANewerOneOnTheSkipList()
    {
        Release("v1.0.0-978");
        Release("v1.0.0-979");
        Release("v1.0.0-980");
        var skipped = ReleaseSkipList.Parse("v1.0.0-979\t2026-10-21\trelease-health exit 2\n");

        PrunePlan plan = ReleaseCache.Plan(
            Updates,
            Facts("v1.0.0-978", "v1.0.0-978", skipped: skipped),
            Now
        );

        PruneItem removed = Assert.Single(
            plan.Items,
            item => item.IsDirectory && !item.Path.EndsWith("extract")
        );
        Assert.Equal(Path.Combine(Updates, "v1.0.0-979"), removed.Path);
        Assert.Contains("skipped-releases.txt", removed.Reason);
        // The staged v1.0.0-980 keeps its zip and extract\ too: the gate may still be unzipping it.
        Assert.DoesNotContain(plan.Items, item => item.Path.Contains("v1.0.0-980"));
    }

    [Fact]
    public void Plan_NeverTouchesAFolderAProcessRunsFrom()
    {
        Release("v1.0.0-900");
        Release("v1.0.0-978");
        IReadOnlyCollection<string> inUse = ReleaseCache.InUse(
            Updates,
            [
                Path.Combine(Updates, "v1.0.0-900", "prepared", "heroesreplay.exe"),
                @"C:\heroesreplay\app\heroesreplay.exe",
                null,
            ]
        );

        PrunePlan plan = ReleaseCache.Plan(
            Updates,
            Facts("v1.0.0-978", "", retained: 0, inUse: inUse),
            Now
        );

        Assert.Equal(new[] { "v1.0.0-900" }, inUse);
        Assert.DoesNotContain(plan.Items, item => item.Path.Contains("v1.0.0-900"));
    }

    [Fact]
    public void Plan_RetainedVersions_KeepsThatManyOlderReleases()
    {
        foreach (int build in new[] { 970, 975, 976, 977, 978 })
        {
            Release($"v1.0.0-{build}");
        }

        PrunePlan plan = ReleaseCache.Plan(Updates, Facts("v1.0.0-978", "", retained: 2), Now);

        Assert.Equal(new[] { "v1.0.0-970", "v1.0.0-975" }, RemovedFolders(plan));
    }

    [Fact]
    public void Plan_ASourceBuildWithNoVersion_RemovesOnlyOldLooseZips()
    {
        Release("v1.0.0-614");
        string old = Write(Path.Combine(Updates, "old.zip"), Now.AddDays(-2));
        string downloading = Write(Path.Combine(Updates, "new.zip"), Now.AddMinutes(-5));

        PrunePlan plan = ReleaseCache.Plan(Updates, Facts("", ""), Now);

        PruneItem item = Assert.Single(plan.Items);
        Assert.Equal(old, item.Path);
        Assert.True(File.Exists(downloading));
    }

    [Theory]
    [InlineData("v1.0.0-978", "v1.0.0-977", 1)]
    [InlineData("v1.0.0-1000", "v1.0.0-999", 1)]
    [InlineData("v1.1.0-1", "v1.0.0-999", 1)]
    [InlineData("v1.0.0", "v1.0.0-0", 0)]
    [InlineData("v1.0.0-5", "v1.0.0-5", 0)]
    public void ReleaseVersion_OrdersByEachNumber(string left, string right, int sign)
    {
        Assert.True(ReleaseVersion.TryParse(left, out ReleaseVersion a));
        Assert.True(ReleaseVersion.TryParse(right, out ReleaseVersion b));
        Assert.Equal(sign, Math.Sign(a.CompareTo(b)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("skipped-releases.txt")]
    [InlineData("v1.0.0-alpha.3")]
    [InlineData("1.0.0")]
    public void ReleaseVersion_RefusesWhatIsNotAReleaseTag(string name)
    {
        Assert.False(ReleaseVersion.TryParse(name, out _));
    }

    [Fact]
    public void PlanLogs_KeepsTheNewestEarlierLogs_AndNeverTheMainOne()
    {
        string logs = Path.Combine(root, "logs");
        string main = Write(Path.Combine(logs, ReleaseCache.MainLogName), Now.AddDays(-30));
        var earlier = new List<string>();
        for (int day = 1; day <= 4; day++)
        {
            earlier.Add(
                Write(
                    Path.Combine(logs, $"apply-release.202610{10 + day}-120000.log"),
                    Now.AddDays(-10 + day)
                )
            );
        }

        string fallback = Write(
            Path.Combine(logs, "apply-release-v1.0.0-950.log"),
            Now.AddDays(-20)
        );
        string role = Write(Path.Combine(logs, "spectate-2026-10-01.log"), Now.AddDays(-20));

        PrunePlan plan = ReleaseCache.PlanLogs(logs, retained: 2);

        Assert.Equal(
            new[] { earlier[0], earlier[1], fallback }.Order(),
            plan.Items.Select(item => item.Path).Order()
        );
        Assert.DoesNotContain(plan.Items, item => item.Path == main || item.Path == role);
    }

    private static ReleaseCacheFacts Facts(
        string installed,
        string previous,
        int retained = 1,
        IReadOnlyCollection<string> inUse = null,
        ReleaseSkipList skipped = null
    ) =>
        new(
            installed,
            previous,
            skipped ?? ReleaseSkipList.Parse(null),
            inUse ?? Array.Empty<string>(),
            retained
        );

    private static IEnumerable<string> RemovedFolders(PrunePlan plan) =>
        plan
            .TopLevel.Where(item => item.IsDirectory && Path.GetFileName(item.Path) != "extract")
            .Select(item => Path.GetFileName(item.Path))
            .Order();

    /// <summary>One staged release as the gate leaves it: the zip, extract\ and prepared\.</summary>
    private void Release(string tag)
    {
        string folder = Path.Combine(Updates, tag);
        Write(Path.Combine(folder, ReleaseSettings.DefaultAssetName), Now.AddDays(-1));
        Write(Path.Combine(folder, "extract", "heroesreplay.exe"), Now.AddDays(-1));
        Write(Path.Combine(folder, "prepared", "heroesreplay.exe"), Now.AddDays(-1));
    }

    private static string Write(string path, DateTimeOffset written)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, new byte[16]);
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
        return path;
    }
}
