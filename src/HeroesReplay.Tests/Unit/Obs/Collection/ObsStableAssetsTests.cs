using System;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs.Collection;
using Xunit;
using static HeroesReplay.Tests.Unit.Obs.Collection.StableAssetsFixture;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

/// <summary>
/// #330: the dev OBS collection pointed at a git worktree that was later removed. With
/// <c>OBS:StableAssets</c> the collection points at a verified copy of the OBS files, and a
/// path-only update repairs paths into a removed worktree.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ObsStableAssetsTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-stable-" + Guid.NewGuid().ToString("N")
    );

    public ObsStableAssetsTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Live));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string Live =>
        Path.Combine(root, "appdata", "obs-studio", "basic", "scenes", "HeroesReplay.json");

    private string Data => Path.Combine(root, "data");

    private ObsManagedFiles Managed => new(Path.Combine(root, "managed"));

    private ObsAssetStore Store => ObsAssetStore.For(Managed);

    [Fact]
    public void ANewCollection_PointsAtTheStableCopy()
    {
        string obs = Install(root, "app", versioned: true);

        ObsCollectionApplyResult result = Apply(obs, stableAssets: true);

        string copy = Store.Planned(obs);
        Assert.True(result.Wrote, result.Message);
        Assert.Contains(
            "Copied the OBS files to " + copy,
            result.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(Forward(copy) + "/Ranks/gold.png", Setting(Live, "gold-image", "file"));
        Assert.Equal(
            Forward(copy) + "/countdown/index.html",
            Setting(Live, "countdown", "local_file")
        );
        Assert.Equal(Forward(Data) + "/OBS.txt", Setting(Live, "current-replay", "file"));
        Assert.True(File.Exists(Path.Combine(copy, "Ranks", "gold.png")));
        Assert.Equal(
            ObsCollectionPatcher.TemplateHash(Path.Combine(obs, "Default.json")),
            Managed.Read(Live).TemplateSha256
        );
    }

    [Fact]
    public void PathsIntoARemovedWorktree_AreRepairedByAPathOnlyUpdate()
    {
        // The dev collection on 2026-10-08: written from an agent's worktree, then the
        // worktree was removed. Before #330 a path update left those absolute paths alone.
        string obs = Install(root, "app", versioned: true);
        string template = File.ReadAllText(Path.Combine(obs, "Default.json"));
        WriteLive(
            ObsCollectionPaths.Rewrite(
                template,
                @"C:\heroesreplay\HeroesReplay\.claude\worktrees\agent-abf31f3ca8421b217\obs",
                Data
            ),
            obs
        );

        ObsCollectionApplyResult result = Apply(obs, stableAssets: true);

        string copy = Store.Planned(obs);
        Assert.True(result.Wrote, result.Message);
        Assert.Null(result.Replacement);
        Assert.Contains("Updated OBS collection paths", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Replaced", result.Message, StringComparison.Ordinal);
        Assert.Equal(Forward(copy) + "/Ranks/gold.png", Setting(Live, "gold-image", "file"));
        Assert.Equal(
            Forward(copy) + "/countdown/index.html",
            Setting(Live, "countdown", "local_file")
        );
        Assert.DoesNotContain("worktrees", File.ReadAllText(Live), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutStableAssets_WorktreePathsMoveToTheInstall()
    {
        string obs = Install(root, "checkout", versioned: false);
        string template = File.ReadAllText(Path.Combine(obs, "Default.json"));
        WriteLive(
            ObsCollectionPaths.Rewrite(template, @"C:\heroesreplay\worktrees\issue-292\obs", Data),
            obs
        );

        ObsCollectionApplyResult result = Apply(obs, stableAssets: false);

        Assert.True(result.Wrote, result.Message);
        Assert.Equal(Forward(obs) + "/Ranks/gold.png", Setting(Live, "gold-image", "file"));
        Assert.False(Directory.Exists(Store.Root));
    }

    [Fact]
    public void TheInstallsOwnFolder_MovesToTheCopy_AsAPathOnlyUpdate_DeferredWhileObsRuns()
    {
        // The stream PC, if OBS:StableAssets is ever turned on there: its collection points at
        // its install's obs folder, written from the same template.
        string obs = Install(root, @"SaltySadism\app", versioned: true);
        string template = File.ReadAllText(Path.Combine(obs, "Default.json"));
        string before = ObsCollectionPaths.Rewrite(template, obs, Data);
        WriteLive(before, obs);
        ObsManagedCollection record = Managed.Read(Live);

        ObsCollectionApplyResult running = Apply(obs, stableAssets: true, obsIsRunning: true);

        // OBS has the collection open: nothing is written, and it is not a live swap.
        Assert.False(running.Wrote);
        Assert.True(running.Deferred);
        Assert.Null(running.Replacement);
        Assert.Equal(before, File.ReadAllText(Live));

        ObsCollectionApplyResult closed = Apply(obs, stableAssets: true);

        string copy = Store.Planned(obs);
        Assert.True(closed.Wrote, closed.Message);
        Assert.Contains("Updated OBS collection paths", closed.Message, StringComparison.Ordinal);
        Assert.Equal(Forward(copy) + "/Ranks/gold.png", Setting(Live, "gold-image", "file"));
        Assert.Equal(record.TemplateSha256, Managed.Read(Live).TemplateSha256);
        Assert.Equal(record.Sources, Managed.Read(Live).Sources);
        // Only the asset paths changed: putting them back gives the old file.
        Assert.Equal(
            before,
            ObsCollectionPaths.Rewrite(File.ReadAllText(Live), obs, Data, [Store.AnyCopy])
        );
    }

    [Fact]
    public void WithoutStableAssets_TheInstallFolderIsKept_AndNothingIsCopied()
    {
        string obs = Install(root, @"SaltySadism\app", versioned: true);
        string before = ObsCollectionPaths.Rewrite(
            File.ReadAllText(Path.Combine(obs, "Default.json")),
            obs,
            Data
        );
        WriteLive(before, obs);

        ObsCollectionApplyResult result = Apply(obs, stableAssets: false);

        Assert.False(result.Wrote);
        Assert.False(result.Drift);
        Assert.Equal(before, File.ReadAllText(Live));
        Assert.False(Directory.Exists(Store.Root));
    }

    [Fact]
    public void TurningStableAssetsOff_MovesThePathsBackToTheInstall()
    {
        string obs = Install(root, "app", versioned: true);
        Apply(obs, stableAssets: true);
        Assert.StartsWith(
            Forward(Store.Root),
            Setting(Live, "gold-image", "file"),
            StringComparison.OrdinalIgnoreCase
        );

        ObsCollectionApplyResult result = Apply(obs, stableAssets: false);

        Assert.True(result.Wrote, result.Message);
        Assert.Equal(Forward(obs) + "/Ranks/gold.png", Setting(Live, "gold-image", "file"));
    }

    [Fact]
    public void AWorktreeInstall_WithNoCopy_IsRefusedAndTheCollectionKept()
    {
        string obs = Install(root, @"repo\.claude\worktrees\agent-x", versioned: true);
        File.Delete(Path.Combine(obs, ObsCollectionBundle.FileName));
        string before = ObsCollectionPaths.Rewrite(
            File.ReadAllText(Path.Combine(obs, "Default.json")),
            @"C:\heroesreplay\HeroesReplay\obs",
            Data
        );
        WriteLive(before, obs);

        ObsCollectionApplyResult withCopy = Apply(obs, stableAssets: true);
        ObsCollectionApplyResult without = Apply(obs, stableAssets: false);

        Assert.True(withCopy.Drift);
        Assert.Contains("Refusing to point OBS at", withCopy.Message, StringComparison.Ordinal);
        Assert.Contains(ObsCollectionBundle.FileName, withCopy.Message, StringComparison.Ordinal);
        Assert.True(without.Drift);
        Assert.Contains("OBS:StableAssets", without.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(Live));
    }

    [Fact]
    public void AWorktreeInstall_WithACopy_PointsAtTheCopy()
    {
        string obs = Install(root, @"repo\.claude\worktrees\agent-x", versioned: false);

        ObsCollectionApplyResult result = Apply(obs, stableAssets: true);

        Assert.True(result.Wrote, result.Message);
        Assert.StartsWith(
            Forward(Store.Root),
            Setting(Live, "gold-image", "file"),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.False(ObsCollectionPaths.IsEphemeral(Setting(Live, "gold-image", "file")));
    }

    [Fact]
    public void ACopyThatCannotBeMade_FallsBackToTheInstallAndSaysWhy()
    {
        string obs = Install(root, "app", versioned: true);
        File.Delete(Path.Combine(obs, ObsCollectionBundle.FileName));

        ObsCollectionApplyResult result = Apply(obs, stableAssets: true);

        Assert.True(result.Wrote, result.Message);
        Assert.Contains(ObsCollectionBundle.FileName, result.Message, StringComparison.Ordinal);
        Assert.Equal(Forward(obs) + "/Ranks/gold.png", Setting(Live, "gold-image", "file"));
    }

    [Fact]
    public void AnUpdate_PrunesUnusedCopies_ButNotOneABackupNames()
    {
        string obs = Install(root, "app", versioned: true);
        string unused = OldCopy("AAAAAAAAAAAAAAAA");
        string backedUp = OldCopy("BBBBBBBBBBBBBBBB");
        Directory.CreateDirectory(Managed.BackupDirectory);
        File.WriteAllText(
            Path.Combine(Managed.BackupDirectory, "scenes-HeroesReplay.json.20261001T000000Z.bak"),
            "{\"file\":\"" + Forward(backedUp) + "/Ranks/gold.png\"}"
        );

        Apply(obs, stableAssets: true);

        Assert.False(Directory.Exists(unused));
        Assert.True(Directory.Exists(backedUp));
        Assert.True(Directory.Exists(Store.Planned(obs)));
    }

    [Fact]
    public void Plan_PreviewsTheCopyWithoutMakingIt()
    {
        string obs = Install(root, "app", versioned: true);
        WriteLive(
            ObsCollectionPaths.Rewrite(
                File.ReadAllText(Path.Combine(obs, "Default.json")),
                obs,
                Data
            ),
            obs
        );
        string planned = Store.Planned(obs);

        ObsCollectionPlanResult plan = ObsCollectionPlan.Build(
            new ObsCollectionPlanRequest
            {
                TemplatePath = Path.Combine(obs, "Default.json"),
                CollectionPath = Live,
                CollectionName = "HeroesReplay",
                DataDirectory = Data,
                Managed = Managed,
                AssetRoot = planned,
                UtcNow = Now,
            }
        );

        Assert.True(plan.Ok, plan.Message);
        Assert.Equal("update_paths", plan.Update.Action);
        Assert.False(Directory.Exists(planned));
    }

    [Theory]
    [InlineData(
        @"C:\heroesreplay\HeroesReplay\.claude\worktrees\agent-abf31f3ca8421b217\obs",
        true
    )]
    [InlineData(@"C:\heroesreplay\worktrees\issue-292\obs", true)]
    [InlineData("C:/heroesreplay/worktrees/develop/obs/Ranks/gold.png", true)]
    [InlineData(@"C:\heroesreplay\HeroesReplay\obs", false)]
    [InlineData(@"C:\SaltySadism\app\obs", false)]
    [InlineData(@"C:\Users\admin\AppData\Local\HeroesReplay\obs\assets\0123456789ABCDEF", false)]
    [InlineData(@"C:\heroesreplay\worktrees", false)]
    [InlineData(null, false)]
    public void Ephemeral_IsAPathInsideAGitWorktree(string path, bool ephemeral)
    {
        Assert.Equal(ephemeral, ObsCollectionPaths.IsEphemeral(path));
    }

    [Theory]
    [InlineData(
        "file",
        "C:/heroesreplay/HeroesReplay/.claude/worktrees/agent-x/obs/Ranks/gold.png",
        "D:/copy/Ranks/gold.png"
    )]
    [InlineData(
        "url",
        "file:///C:/heroesreplay/worktrees/rc/obs/countdown/index.html?m=2&s=0",
        "file:///D:/copy/countdown/index.html?m=2&s=0"
    )]
    [InlineData(
        "file",
        "C:/Users/admin/AppData/Local/HeroesReplay/obs/assets/0123456789ABCDEF/Ranks/gold.png",
        "D:/copy/Ranks/gold.png"
    )]
    [InlineData("file", "C:/SaltySadism/app/obs/Ranks/gold.png", "D:/copy/Ranks/gold.png")]
    // Not a packaged asset, data, the web, and another folder stay as they are.
    [InlineData(
        "file",
        "C:/heroesreplay/worktrees/rc/obs/Default/basic.ini",
        "C:/heroesreplay/worktrees/rc/obs/Default/basic.ini"
    )]
    [InlineData("file", "C:/heroesreplay/Data/OBS.txt", "C:/data/OBS.txt")]
    [InlineData(
        "url",
        "https://www.heroesprofile.com/x.png",
        "https://www.heroesprofile.com/x.png"
    )]
    [InlineData("file", "D:/my-images/gold.png", "D:/my-images/gold.png")]
    public void RewriteValue_MovesWorktreeCopyAndGivenFolders(
        string property,
        string value,
        string expected
    )
    {
        Assert.Equal(
            expected,
            ObsCollectionPaths.RewriteValue(
                property,
                value,
                "D:/copy",
                "C:/data",
                [
                    @"C:\Users\admin\AppData\Local\HeroesReplay\obs\assets\*",
                    @"C:\SaltySadism\app\obs",
                ]
            )
        );
    }

    private ObsCollectionApplyResult Apply(
        string obs,
        bool stableAssets,
        bool obsIsRunning = false
    ) =>
        ObsCollectionPatcher.Apply(
            new ObsCollectionUpdate
            {
                TemplatePath = Path.Combine(obs, "Default.json"),
                DestinationPath = Live,
                DataDirectory = Data,
                ObsIsRunning = obsIsRunning,
                CollectionName = "HeroesReplay",
                Managed = Managed,
                StableAssets = stableAssets,
                UtcNow = Now,
            }
        );

    /// <summary>The live collection, recorded as written from <paramref name="obs"/>'s template.</summary>
    private void WriteLive(string json, string obs)
    {
        File.WriteAllText(Live, json);
        string template = Path.Combine(obs, "Default.json");
        Managed.Save(
            Live,
            new ObsManagedCollection(
                ObsCollectionPatcher.TemplateHash(template),
                ObsCollectionPaths
                    .SourceNames(File.ReadAllText(template))
                    .Order(StringComparer.Ordinal)
                    .ToList(),
                Now.AddDays(-1)
            )
        );
    }

    private string OldCopy(string hash)
    {
        string folder = Path.Combine(Store.Root, hash);
        Directory.CreateDirectory(folder);
        string marker = Path.Combine(folder, ObsAssetStore.MarkerFileName);
        File.WriteAllText(marker, "old");
        File.SetLastWriteTimeUtc(marker, Now.AddDays(-10));
        return folder;
    }
}
